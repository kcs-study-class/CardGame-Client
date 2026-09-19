// KcsVideo - 暗号化 VP8 動画 (.kcsv) デコーダ実装
#include "kcsv.h"

#include <algorithm>
#include <cstdio>
#include <cstring>
#include <string>
#include <vector>

extern "C" {
#include "aes.h"
}
#include "vpx/vp8dx.h"
#include "vpx/vpx_decoder.h"

#if defined(_WIN32)
#  define WIN32_LEAN_AND_MEAN
#  include <windows.h>
#  define KCSV_FSEEK _fseeki64
#  define KCSV_FTELL _ftelli64
#else
#  define KCSV_FSEEK fseeko
#  define KCSV_FTELL ftello
#endif

namespace {

constexpr uint32_t IVF_HEADER_SIZE = 32;
constexpr uint32_t IVF_FRAME_HEADER_SIZE = 12;
constexpr uint64_t AES_BLOCK = 16;
constexpr int32_t LIBRARY_VERSION = 100; // 0.1.0

struct FrameEntry {
    uint64_t dataOffset; // ペイロード内オフセット (12 byte のフレームヘッダの後)
    uint32_t size;
    int64_t ptsMs;
    bool keyframe;
};

uint16_t rd16(const uint8_t* p) { return static_cast<uint16_t>(p[0] | (p[1] << 8)); }
uint32_t rd32(const uint8_t* p) {
    return static_cast<uint32_t>(p[0]) | (static_cast<uint32_t>(p[1]) << 8) |
           (static_cast<uint32_t>(p[2]) << 16) | (static_cast<uint32_t>(p[3]) << 24);
}
uint64_t rd64(const uint8_t* p) { return static_cast<uint64_t>(rd32(p)) | (static_cast<uint64_t>(rd32(p + 4)) << 32); }

// 128bit ビッグエンディアンのカウンタに blocks を加算する (tiny-AES の CTR インクリメントと同じ並び)
void counterAdd(uint8_t iv[16], uint64_t blocks) {
    uint64_t carry = blocks;
    for (int i = 15; i >= 0 && carry != 0; --i) {
        uint64_t sum = static_cast<uint64_t>(iv[i]) + (carry & 0xFF);
        iv[i] = static_cast<uint8_t>(sum & 0xFF);
        carry = (carry >> 8) + (sum >> 8);
    }
}

FILE* openFileUtf8(const char* path) {
#if defined(_WIN32)
    int length = MultiByteToWideChar(CP_UTF8, 0, path, -1, nullptr, 0);
    if (length <= 0) {
        return nullptr;
    }
    std::wstring wide(static_cast<size_t>(length), L'\0');
    MultiByteToWideChar(CP_UTF8, 0, path, -1, &wide[0], length);
    return _wfopen(wide.c_str(), L"rb");
#else
    return fopen(path, "rb");
#endif
}

uint8_t clampByte(int value) {
    return static_cast<uint8_t>(value < 0 ? 0 : (value > 255 ? 255 : value));
}

} // namespace

struct kcsv_handle {
    FILE* file = nullptr;
    uint8_t key[KCSV_KEY_SIZE] = {};
    uint8_t nonce[KCSV_NONCE_SIZE] = {};
    uint64_t payloadLength = 0;
    kcsv_info info = {};
    std::vector<FrameEntry> frames;
    size_t nextIndex = 0;
    vpx_codec_ctx_t codec = {};
    bool codecReady = false;
    std::vector<uint8_t> ioBuffer;
    std::vector<uint8_t> frameBuffer;
    std::string lastError;

    void setError(const char* message) { lastError = message != nullptr ? message : ""; }

    // ペイロード内 [offset, offset+length) を復号して out に書く
    bool readDecrypted(uint64_t offset, size_t length, uint8_t* out) {
        if (offset + length > payloadLength) {
            setError("read out of payload range");
            return false;
        }
        uint64_t blockStart = (offset / AES_BLOCK) * AES_BLOCK;
        size_t span = static_cast<size_t>(offset + length - blockStart);
        ioBuffer.resize(span);
        if (KCSV_FSEEK(file, static_cast<long long>(KCSV_HEADER_SIZE + blockStart), SEEK_SET) != 0) {
            setError("seek failed");
            return false;
        }
        if (fread(ioBuffer.data(), 1, span, file) != span) {
            setError("read failed (truncated file?)");
            return false;
        }
        uint8_t iv[KCSV_NONCE_SIZE];
        memcpy(iv, nonce, KCSV_NONCE_SIZE);
        counterAdd(iv, blockStart / AES_BLOCK);
        AES_ctx ctx;
        AES_init_ctx_iv(&ctx, key, iv);
        AES_CTR_xcrypt_buffer(&ctx, ioBuffer.data(), span);
        memcpy(out, ioBuffer.data() + (offset - blockStart), length);
        return true;
    }

    bool initCodec() {
        if (codecReady) {
            vpx_codec_destroy(&codec);
            codecReady = false;
        }
        if (vpx_codec_dec_init(&codec, vpx_codec_vp8_dx(), nullptr, 0) != VPX_CODEC_OK) {
            setError("vpx_codec_dec_init failed");
            return false;
        }
        codecReady = true;
        return true;
    }

    int64_t toMs(uint64_t pts) const {
        if (info.timebaseDen <= 0) {
            return 0;
        }
        // pts * 1000 * num / den。オーバーフロー対策で 128bit 相当の分割計算はせず、
        // 実用範囲 (pts < 2^43) なら 64bit に収まる
        return static_cast<int64_t>((pts * 1000ULL * static_cast<uint64_t>(info.timebaseNum)) /
                                    static_cast<uint64_t>(info.timebaseDen));
    }

    bool buildIndex() {
        uint8_t ivf[IVF_HEADER_SIZE];
        if (payloadLength < IVF_HEADER_SIZE || !readDecrypted(0, IVF_HEADER_SIZE, ivf)) {
            setError("payload too small for IVF header");
            return false;
        }
        if (memcmp(ivf, "DKIF", 4) != 0) {
            setError("not an IVF stream (wrong key?)");
            return false;
        }
        if (memcmp(ivf + 8, "VP80", 4) != 0) {
            setError("IVF fourcc is not VP80");
            return false;
        }
        info.width = rd16(ivf + 12);
        info.height = rd16(ivf + 14);
        info.timebaseDen = static_cast<int32_t>(rd32(ivf + 16)); // rate
        info.timebaseNum = static_cast<int32_t>(rd32(ivf + 20)); // scale
        if (info.width <= 0 || info.height <= 0 || info.timebaseDen <= 0 || info.timebaseNum <= 0) {
            setError("invalid IVF header values");
            return false;
        }

        uint32_t declaredFrames = rd32(ivf + 24);
        frames.clear();
        frames.reserve(declaredFrames > 0 && declaredFrames < 1000000 ? declaredFrames : 256);

        uint64_t pos = IVF_HEADER_SIZE;
        uint8_t head[IVF_FRAME_HEADER_SIZE + 1];
        int32_t keyframes = 0;
        while (pos + IVF_FRAME_HEADER_SIZE <= payloadLength) {
            if (!readDecrypted(pos, IVF_FRAME_HEADER_SIZE, head)) {
                return false;
            }
            uint32_t size = rd32(head);
            uint64_t pts = rd64(head + 4);
            uint64_t dataOffset = pos + IVF_FRAME_HEADER_SIZE;
            if (size == 0 || dataOffset + size > payloadLength) {
                break; // 末尾の不完全なフレームは無視
            }
            if (!readDecrypted(dataOffset, 1, head + IVF_FRAME_HEADER_SIZE)) {
                return false;
            }
            FrameEntry entry;
            entry.dataOffset = dataOffset;
            entry.size = size;
            entry.ptsMs = toMs(pts);
            entry.keyframe = (head[IVF_FRAME_HEADER_SIZE] & 0x01) == 0; // VP8: bit0 = 0 がキーフレーム
            if (entry.keyframe) {
                ++keyframes;
            }
            frames.push_back(entry);
            pos = dataOffset + size;
        }
        if (frames.empty()) {
            setError("no frames");
            return false;
        }
        info.frameCount = static_cast<int32_t>(frames.size());
        info.keyframeCount = keyframes;
        if (frames.size() >= 2) {
            int64_t last = frames[frames.size() - 1].ptsMs;
            int64_t prev = frames[frames.size() - 2].ptsMs;
            info.durationMs = last + std::max<int64_t>(0, last - prev);
        } else {
            info.durationMs = toMs(1);
        }
        return true;
    }

    // 現在位置のフレームを libvpx に渡す (出力画像の取り出しは呼び出し側)
    int32_t decodeCurrent() {
        const FrameEntry& entry = frames[nextIndex];
        frameBuffer.resize(entry.size);
        if (!readDecrypted(entry.dataOffset, entry.size, frameBuffer.data())) {
            return KCSV_E_IO;
        }
        ++nextIndex;
        if (vpx_codec_decode(&codec, frameBuffer.data(), entry.size, nullptr, 0) != VPX_CODEC_OK) {
            const char* detail = vpx_codec_error_detail(&codec);
            lastError = std::string("vpx_codec_decode: ") + vpx_codec_error(&codec) +
                        (detail != nullptr ? std::string(" / ") + detail : std::string());
            return KCSV_E_CODEC;
        }
        return KCSV_OK;
    }
};

// ---- I420 → RGBA (BT.601 limited range)。Unity の Texture2D 向けに下の行から書く ----
static void convertI420ToRgba(const vpx_image_t* img, uint8_t* rgba, int32_t width, int32_t height) {
    const int32_t copyWidth = std::min<int32_t>(width, static_cast<int32_t>(img->d_w));
    const int32_t copyHeight = std::min<int32_t>(height, static_cast<int32_t>(img->d_h));
    for (int32_t y = 0; y < copyHeight; ++y) {
        const uint8_t* rowY = img->planes[VPX_PLANE_Y] + static_cast<size_t>(y) * img->stride[VPX_PLANE_Y];
        const uint8_t* rowU = img->planes[VPX_PLANE_U] + static_cast<size_t>(y / 2) * img->stride[VPX_PLANE_U];
        const uint8_t* rowV = img->planes[VPX_PLANE_V] + static_cast<size_t>(y / 2) * img->stride[VPX_PLANE_V];
        uint8_t* out = rgba + static_cast<size_t>(height - 1 - y) * static_cast<size_t>(width) * 4;
        for (int32_t x = 0; x < copyWidth; ++x) {
            int c = static_cast<int>(rowY[x]) - 16;
            int d = static_cast<int>(rowU[x / 2]) - 128;
            int e = static_cast<int>(rowV[x / 2]) - 128;
            out[0] = clampByte((298 * c + 409 * e + 128) >> 8);
            out[1] = clampByte((298 * c - 100 * d - 208 * e + 128) >> 8);
            out[2] = clampByte((298 * c + 516 * d + 128) >> 8);
            out[3] = 255;
            out += 4;
        }
    }
}

// ---- C API ----

extern "C" {

KCSV_API int32_t kcsv_version(void) { return LIBRARY_VERSION; }

KCSV_API int32_t kcsv_open(const char* utf8Path, const uint8_t* key32, kcsv_handle** outHandle) {
    if (utf8Path == nullptr || key32 == nullptr || outHandle == nullptr) {
        return KCSV_E_ARG;
    }
    *outHandle = nullptr;
    kcsv_handle* h = new (std::nothrow) kcsv_handle();
    if (h == nullptr) {
        return KCSV_E_NOMEM;
    }
    memcpy(h->key, key32, KCSV_KEY_SIZE);

    h->file = openFileUtf8(utf8Path);
    if (h->file == nullptr) {
        delete h;
        return KCSV_E_IO;
    }

    uint8_t header[KCSV_HEADER_SIZE];
    if (fread(header, 1, KCSV_HEADER_SIZE, h->file) != KCSV_HEADER_SIZE || memcmp(header, "KCSV", 4) != 0) {
        kcsv_close(h);
        return KCSV_E_FORMAT;
    }
    if (rd16(header + 4) != KCSV_VERSION) {
        kcsv_close(h);
        return KCSV_E_FORMAT;
    }
    memcpy(h->nonce, header + 8, KCSV_NONCE_SIZE);
    h->payloadLength = rd64(header + 56);

    if (KCSV_FSEEK(h->file, 0, SEEK_END) != 0) {
        kcsv_close(h);
        return KCSV_E_IO;
    }
    long long fileSize = KCSV_FTELL(h->file);
    if (fileSize < 0 || static_cast<uint64_t>(fileSize) < KCSV_HEADER_SIZE + h->payloadLength) {
        kcsv_close(h);
        return KCSV_E_FORMAT;
    }

    if (!h->buildIndex()) {
        int32_t code = h->lastError.find("read failed") != std::string::npos ? KCSV_E_IO : KCSV_E_FORMAT;
        kcsv_close(h);
        return code;
    }
    if (!h->initCodec()) {
        kcsv_close(h);
        return KCSV_E_CODEC;
    }
    *outHandle = h;
    return KCSV_OK;
}

KCSV_API int32_t kcsv_get_info(kcsv_handle* handle, kcsv_info* outInfo) {
    if (handle == nullptr || outInfo == nullptr) {
        return KCSV_E_ARG;
    }
    *outInfo = handle->info;
    return KCSV_OK;
}

KCSV_API int32_t kcsv_decode_next(kcsv_handle* handle, uint8_t* rgbaOut, size_t rgbaSize, int64_t* outPtsMs) {
    if (handle == nullptr || rgbaOut == nullptr) {
        return KCSV_E_ARG;
    }
    if (handle->nextIndex >= handle->frames.size()) {
        return KCSV_EOF;
    }
    const size_t required = static_cast<size_t>(handle->info.width) * static_cast<size_t>(handle->info.height) * 4;
    if (rgbaSize < required) {
        handle->setError("rgba buffer too small");
        return KCSV_E_BUFFER;
    }
    const int64_t ptsMs = handle->frames[handle->nextIndex].ptsMs;
    int32_t result = handle->decodeCurrent();
    if (result != KCSV_OK) {
        return result;
    }
    vpx_codec_iter_t iter = nullptr;
    vpx_image_t* img = vpx_codec_get_frame(&handle->codec, &iter);
    if (img == nullptr) {
        return KCSV_NO_FRAME;
    }
    convertI420ToRgba(img, rgbaOut, handle->info.width, handle->info.height);
    if (outPtsMs != nullptr) {
        *outPtsMs = ptsMs;
    }
    return KCSV_OK;
}

KCSV_API int32_t kcsv_seek(kcsv_handle* handle, int64_t targetMs, int32_t precise) {
    if (handle == nullptr) {
        return KCSV_E_ARG;
    }
    size_t keyIndex = 0;
    for (size_t i = 0; i < handle->frames.size(); ++i) {
        if (handle->frames[i].ptsMs > targetMs) {
            break;
        }
        if (handle->frames[i].keyframe) {
            keyIndex = i;
        }
    }
    if (!handle->initCodec()) { // 参照フレームを捨てる
        return KCSV_E_CODEC;
    }
    handle->nextIndex = keyIndex;
    if (precise == 0) {
        return KCSV_OK;
    }
    // 「targetMs 時点で表示されているフレーム」が次に返るようにデコードを進める
    while (handle->nextIndex + 1 < handle->frames.size() && handle->frames[handle->nextIndex + 1].ptsMs <= targetMs) {
        int32_t result = handle->decodeCurrent();
        if (result != KCSV_OK) {
            return result;
        }
        vpx_codec_iter_t iter = nullptr;
        while (vpx_codec_get_frame(&handle->codec, &iter) != nullptr) {
            // 画像は捨てる
        }
    }
    return KCSV_OK;
}

KCSV_API int32_t kcsv_get_next_frame_index(kcsv_handle* handle) {
    if (handle == nullptr) {
        return KCSV_E_ARG;
    }
    return static_cast<int32_t>(handle->nextIndex);
}

KCSV_API const char* kcsv_get_last_error(kcsv_handle* handle) {
    return handle != nullptr ? handle->lastError.c_str() : "";
}

KCSV_API void kcsv_close(kcsv_handle* handle) {
    if (handle == nullptr) {
        return;
    }
    if (handle->codecReady) {
        vpx_codec_destroy(&handle->codec);
        handle->codecReady = false;
    }
    if (handle->file != nullptr) {
        fclose(handle->file);
        handle->file = nullptr;
    }
    delete handle;
}

} // extern "C"
