cmake_minimum_required(VERSION 3.16)
project(TIMF_Bootstrap LANGUAGES CXX)

add_library(TIMF.Bootstrap SHARED bootstrap.cpp)
set_target_properties(TIMF.Bootstrap PROPERTIES
  OUTPUT_NAME "TIMF.Bootstrap"
  PREFIX ""
)

target_compile_definitions(TIMF.Bootstrap PRIVATE WIN32_LEAN_AND_MEAN UNICODE _UNICODE)
target_link_libraries(TIMF.Bootstrap PRIVATE mscoree ole32 oleaut32)

# Force 32-bit when using MinGW i686 toolchain.
if(MINGW)
  target_compile_options(TIMF.Bootstrap PRIVATE -m32)
  target_link_options(TIMF.Bootstrap PRIVATE -m32 -static-libgcc -static-libstdc++ -Wl,--kill-at)
endif()
