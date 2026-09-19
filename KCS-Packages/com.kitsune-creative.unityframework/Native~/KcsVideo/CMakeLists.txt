cmake_minimum_required(VERSION 3.20)
project(KcsVideo VERSION 0.1.0 LANGUAGES C CXX)

# KcsVideo: 暗号化 VP8 動画 (.kcsv) デコーダの Unity ネイティブプラグイン
#   Windows : 共有ライブラリ KcsVideo.dll  (→ Runtime/Plugins/x86_64/)
#   iOS     : 静的ライブラリ libKcsVideo.a (→ Runtime/Plugins/iOS/, C# 側は __Internal)
# 依存: libvpx (vcpkg 推奨。scripts/build-windows.ps1 参照) / tiny-AES-c (同梱, Unlicense)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_C_STANDARD 11)

option(KCSV_SHARED "共有ライブラリとしてビルドする (Unity の DLL プラグイン)" ON)
set(KCSV_LIBVPX_ROOT "" CACHE PATH "libvpx のインストール先 (vcpkg の installed/<triplet>)")

if(KCSV_LIBVPX_ROOT)
    list(APPEND CMAKE_PREFIX_PATH "${KCSV_LIBVPX_ROOT}")
endif()

# ---- libvpx ----
find_package(unofficial-libvpx CONFIG QUIET)
if(TARGET unofficial::libvpx::libvpx)
    set(KCSV_VPX_TARGET unofficial::libvpx::libvpx)
    message(STATUS "libvpx: vcpkg config package")
else()
    find_path(VPX_INCLUDE_DIR vpx/vpx_decoder.h REQUIRED)
    find_library(VPX_LIBRARY NAMES vpx vpxmd vpxmt libvpx REQUIRED)
    add_library(kcsv_vpx STATIC IMPORTED)
    set_target_properties(kcsv_vpx PROPERTIES
        IMPORTED_LOCATION "${VPX_LIBRARY}"
        INTERFACE_INCLUDE_DIRECTORIES "${VPX_INCLUDE_DIR}")
    set(KCSV_VPX_TARGET kcsv_vpx)
    message(STATUS "libvpx: ${VPX_LIBRARY}")
endif()

# ---- ライブラリ本体 ----
set(KCSV_SOURCES
    src/kcsv.cpp
    src/kcsv.h
    third_party/tiny-AES-c/aes.c
    third_party/tiny-AES-c/aes.h)

if(KCSV_SHARED)
    add_library(KcsVideo SHARED ${KCSV_SOURCES})
    target_compile_definitions(KcsVideo PRIVATE KCSV_BUILD_SHARED=1)
else()
    add_library(KcsVideo STATIC ${KCSV_SOURCES})
endif()

target_include_directories(KcsVideo
    PUBLIC "${CMAKE_CURRENT_SOURCE_DIR}/src"
    PRIVATE "${CMAKE_CURRENT_SOURCE_DIR}/third_party/tiny-AES-c")
# tiny-AES-c: AES-256 / CTR のみ有効化
target_compile_definitions(KcsVideo PRIVATE AES256=1 CTR=1 ECB=0 CBC=0)
target_link_libraries(KcsVideo PRIVATE ${KCSV_VPX_TARGET})

if(WIN32)
    # MinGW でも "libKcsVideo.dll" にならないようにする
    set_target_properties(KcsVideo PROPERTIES PREFIX "")
endif()

if(MSVC)
    # vcredist 不要にするため静的 CRT (vcpkg の x64-windows-static と合わせる)
    set_property(TARGET KcsVideo PROPERTY MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
    target_compile_options(KcsVideo PRIVATE /W4 /utf-8)
else()
    target_compile_options(KcsVideo PRIVATE -Wall -Wextra -fvisibility=hidden)
endif()

if(CMAKE_SYSTEM_NAME STREQUAL "iOS")
    set_target_properties(KcsVideo PROPERTIES XCODE_ATTRIBUTE_ENABLE_BITCODE "NO")
endif()

install(TARGETS KcsVideo
    RUNTIME DESTINATION bin
    LIBRARY DESTINATION lib
    ARCHIVE DESTINATION lib)
