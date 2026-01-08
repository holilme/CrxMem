cmake_minimum_required(VERSION 3.15)
project(VEHDebug)

set(CMAKE_CXX_STANDARD 17)

# Build both 32-bit and 64-bit DLLs
add_library(VEHDebug64 SHARED
    VEHDebug.cpp
    VEHDebug.h
    VEHDebug.def
)

target_link_libraries(VEHDebug64 PRIVATE
    kernel32
)

set_target_properties(VEHDebug64 PROPERTIES
    OUTPUT_NAME "VEHDebug64"
    RUNTIME_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/bin"
)
