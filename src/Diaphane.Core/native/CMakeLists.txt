cmake_minimum_required(VERSION 3.21)
project(DiaphaneCore CXX)

# --- CEF SDK (built by engine/scripts/, lives outside the repo) ---
set(DIAPHANE_CEF_SDK "F:/cef-build/dist/cef" CACHE PATH "CEF binary SDK root")
if(NOT EXISTS "${DIAPHANE_CEF_SDK}/include/cef_version.h")
  message(FATAL_ERROR "CEF SDK not found at ${DIAPHANE_CEF_SDK} (set -DDIAPHANE_CEF_SDK=...)")
endif()

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded")  # /MT — CEF requires the static CRT on Windows
add_compile_definitions(UNICODE _UNICODE WIN32 NOMINMAX WIN32_LEAN_AND_MEAN
                        _WIN32_WINNT=0x0A00 WINVER=0x0A00)
add_compile_options(/EHsc /bigobj /utf-8 /W3 /wd4100)

# --- libcef_dll_wrapper: rebuilt with THIS toolchain so its STL/CRT matches ours ---
# Exact client-side source list (a glob pulls in DLL-side-only cpptoc/ctocpp files
# and the bootstrap/ + sandbox/ trees that need full Chromium //base).
include("${CMAKE_CURRENT_SOURCE_DIR}/cef_wrapper_sources.cmake")
add_library(libcef_dll_wrapper STATIC ${CEF_WRAPPER_SOURCES})
target_compile_definitions(libcef_dll_wrapper PRIVATE WRAPPING_CEF_SHARED)
target_include_directories(libcef_dll_wrapper PUBLIC "${DIAPHANE_CEF_SDK}")

# --- the bridge DLL ---
add_library(DiaphaneCore SHARED
    src/diaphane_core.cc
    src/dc_app.cc
    src/dc_client.cc)
target_compile_definitions(DiaphaneCore PRIVATE DIAPHANE_CORE_IMPL)
target_include_directories(DiaphaneCore PRIVATE
    "${CMAKE_CURRENT_SOURCE_DIR}"        # for "src/..." and "include/diaphane_core.h"
    "${DIAPHANE_CEF_SDK}")               # for "include/cef_*.h" and "libcef_dll/..."
target_link_libraries(DiaphaneCore PRIVATE
    libcef_dll_wrapper
    "${DIAPHANE_CEF_SDK}/Release/libcef.dll.lib")

# --- CEF sub-process host ---
add_executable(diaphane_helper WIN32 helper/helper.cc)
target_include_directories(diaphane_helper PRIVATE "${DIAPHANE_CEF_SDK}")
target_link_libraries(diaphane_helper PRIVATE
    libcef_dll_wrapper
    "${DIAPHANE_CEF_SDK}/Release/libcef.dll.lib")

# --- stage a runnable layout in bin/ ---
set(STAGE "${CMAKE_BINARY_DIR}/bin")
add_custom_command(TARGET DiaphaneCore POST_BUILD
  COMMAND ${CMAKE_COMMAND} -E make_directory "${STAGE}"
  COMMAND ${CMAKE_COMMAND} -E copy_if_different
      "$<TARGET_FILE:DiaphaneCore>" "$<TARGET_FILE:diaphane_helper>" "${STAGE}"
  COMMAND ${CMAKE_COMMAND} -E copy_directory "${DIAPHANE_CEF_SDK}/Release" "${STAGE}"
  COMMAND ${CMAKE_COMMAND} -E copy_directory "${DIAPHANE_CEF_SDK}/Resources" "${STAGE}"
  VERBATIM)
add_dependencies(DiaphaneCore diaphane_helper)
