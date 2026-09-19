// KcsVideo - 暗号化 VP8 動画 (.kcsv) のデコードライブラリ
//
// コンテナ: IVF (VP8 フレーム列) を AES-256-CTR で暗号化したもの。
// ヘッダ 64 byte (平文) + 暗号化ペイロード (IVF そのもの)。
//
//   offset  size  内容
//   0       4     magic "KCSV"
//   4       2     version (u16 LE) = 1
//   6       2     flags (u16 LE)   bit0 = HMAC あり (検証は C# 側で行う)
//   8       16    nonce (AES-CTR の初期カウンタ)
//   24      32    HMAC-SHA256(ヘッダ先頭 24 byte + 暗号文)  flags bit0 が 0 なら全て 0
//   56      8     payloadLength (u64 LE) = 暗号文の長さ (= 元の IVF の長さ)
//   64      -     暗号文
//
// CTR なので任意オフセットから復号でき、シークが容易。
// 全 API はスレッドセーフではない (1 ハンドル = 1 スレッドから使うこと)。
#ifndef KCSV_H
#define KCSV_H

#include <stddef.h>
#include <stdint.h>

#if defined(_WIN32)
#  if defined(KCSV_BUILD_SHARED)
#    define KCSV_API __declspec(dllexport)
#  else
#    define KCSV_API
#  endif
#else
#  define KCSV_API __attribute__((visibility("default")))
#endif

#ifdef __cplusplus
extern "C" {
#endif

#define KCSV_HEADER_SIZE 64
#define KCSV_KEY_SIZE 32
#define KCSV_NONCE_SIZE 16
#define KCSV_VERSION 1

// 戻り値
#define KCSV_OK 0
#define KCSV_EOF 1          // kcsv_decode_next: フレームが尽きた
#define KCSV_NO_FRAME 2     // kcsv_decode_next: デコードしたが出力画像なし (呼び直す)
#define KCSV_E_ARG -1
#define KCSV_E_IO -2
#define KCSV_E_FORMAT -3    // マジック/バージョン不一致、IVF でない (鍵違いも大抵ここ)
#define KCSV_E_CODEC -4     // libvpx エラー
#define KCSV_E_BUFFER -5    // 出力バッファ不足
#define KCSV_E_NOMEM -6

typedef struct kcsv_handle kcsv_handle;

typedef struct kcsv_info {
    int32_t width;
    int32_t height;
    int32_t frameCount;
    int32_t timebaseNum;     // 1 フレームの時間 = pts * timebaseNum / timebaseDen [秒]
    int32_t timebaseDen;
    int64_t durationMs;
    int32_t keyframeCount;
    int32_t reserved;
} kcsv_info;

// ライブラリのバージョン (メジャー*10000 + マイナー*100 + パッチ)
KCSV_API int32_t kcsv_version(void);

// ファイルを開き、フレームインデックスを構築する。key は 32 byte (AES-256)。
// 成功時 KCSV_OK と *outHandle を返す。
KCSV_API int32_t kcsv_open(const char* utf8Path, const uint8_t* key32, kcsv_handle** outHandle);

// 動画情報を取得する。
KCSV_API int32_t kcsv_get_info(kcsv_handle* handle, kcsv_info* outInfo);

// 次のフレームをデコードし RGBA32 (width*height*4 byte) で書き込む。
// 行順は Unity の Texture2D.LoadRawTextureData に合わせて下から上 (bottom-up)。
// 戻り値: KCSV_OK = 1 フレーム出力 / KCSV_EOF = 終端 / KCSV_NO_FRAME = 出力なし (続けて呼ぶ) / 負 = エラー。
// outPtsMs には出力フレームの表示時刻 [ms] が入る。
KCSV_API int32_t kcsv_decode_next(kcsv_handle* handle, uint8_t* rgbaOut, size_t rgbaSize, int64_t* outPtsMs);

// 指定時刻 [ms] 以前の直近キーフレームへ移動する。precise が非 0 なら、
// その後 targetMs 直前までのフレームを (出力せずに) デコードして位置を合わせる。
KCSV_API int32_t kcsv_seek(kcsv_handle* handle, int64_t targetMs, int32_t precise);

// 次に kcsv_decode_next が返すフレームの index (0 始まり)。終端なら frameCount。
KCSV_API int32_t kcsv_get_next_frame_index(kcsv_handle* handle);

// 直近のエラーメッセージ (ハンドルが生きている間有効。エラーなしなら空文字)。
KCSV_API const char* kcsv_get_last_error(kcsv_handle* handle);

KCSV_API void kcsv_close(kcsv_handle* handle);

#ifdef __cplusplus
}
#endif

#endif // KCSV_H
