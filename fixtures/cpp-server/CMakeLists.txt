cmake_minimum_required(VERSION 3.24)

project(thingset-fixture-server LANGUAGES C CXX)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

# Which thingset++ to build against. Defaults to a pinned commit; CI's "latest"
# job passes -DTHINGSETPLUSPLUS_GIT_TAG=main. To build against a local checkout
# instead, pass -DFETCHCONTENT_SOURCE_DIR_THINGSETPLUSPLUS=/path/to/thingsetplusplus.
set(THINGSETPLUSPLUS_GIT_REPOSITORY "https://github.com/Brill-Power/thingsetplusplus.git"
    CACHE STRING "thingset++ git repository")
set(THINGSETPLUSPLUS_GIT_TAG "f74b743de7f2e5b70c5b85c51abc1e7534853ab5"
    CACHE STRING "thingset++ git ref (commit, tag or branch)")

# thingset++ build options
set(ENABLE_ASIO ON)
set(ENABLE_SERVER ON)

include(FetchContent)
FetchContent_Declare(thingsetplusplus
    GIT_REPOSITORY ${THINGSETPLUSPLUS_GIT_REPOSITORY}
    GIT_TAG ${THINGSETPLUSPLUS_GIT_TAG}
    # googletest is only needed for thingset++'s own tests
    GIT_SUBMODULES "asio" "zcbor"
    GIT_SUBMODULES_RECURSE OFF
)
FetchContent_MakeAvailable(thingsetplusplus)

add_executable(thingset-fixture-server main.cpp)

# thingset++ uses directory-scoped include_directories(), which don't reach us.
target_include_directories(thingset-fixture-server PRIVATE
    ${thingsetplusplus_SOURCE_DIR}/include
    ${thingsetplusplus_SOURCE_DIR}/zcbor/include
    ${thingsetplusplus_SOURCE_DIR}/asio/asio/include)

target_link_libraries(thingset-fixture-server PRIVATE thingset++)
