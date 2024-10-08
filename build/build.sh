#!/bin/bash

set -exu

VERSION=$1
OS=linux
TIMESTAMP="$(date +%Y%m%d)"
AOT_BUILD_NEEDED=1

if echo "${VERSION}" | grep -q 'trunk'; then
    VERSION=trunk-"$TIMESTAMP"
    BRANCH=main
    VERSION_WITHOUT_V="${VERSION}"
else
    BRANCH="${VERSION}"
    VERSION_WITHOUT_V="${VERSION:1}"
    MAJOR_VERSION="${VERSION_WITHOUT_V%%.*}"
    if [[ "${MAJOR_VERSION}" -lt 8 ]]; then OS=Linux; fi
    if [[ "${MAJOR_VERSION}" -lt 7 ]]; then AOT_BUILD_NEEDED=0; fi
fi

URL=https://github.com/dotnet/runtime.git

FULLNAME=dotnet-${VERSION}.tar.xz
OUTPUT=$2/${FULLNAME}

DOTNET_REVISION=$(git ls-remote --heads ${URL} refs/heads/"${BRANCH}" | cut -f 1)
REVISION="dotnet-${DOTNET_REVISION}"
LAST_REVISION="${3:-}"

echo "ce-build-revision:${REVISION}"
echo "ce-build-output:${OUTPUT}"

if [[ "${REVISION}" == "${LAST_REVISION}" ]]; then
    echo "ce-build-status:SKIPPED"
    exit
fi

DIR=$(pwd)/dotnet/runtime

git clone --depth 1 -b "${BRANCH}" ${URL} "${DIR}"
cd "${DIR}"

commit="$(git rev-parse HEAD)"
echo "HEAD is at: $commit"

CORE_ROOT="$(pwd)"/artifacts/tests/coreclr/"${OS}".x64.Release/Tests/Core_Root
CORE_ROOT_MONO="$(pwd)"/artifacts/tests/mono/"${OS}".x64.Release/Tests/Core_Root

# Build everything in Release mode
if [[ "$AOT_BUILD_NEEDED" -eq 1 ]]; then
  ./build.sh Clr+Clr.Aot+Libs+Mono -c Release --ninja -ci -p:OfficialBuildId="$TIMESTAMP"-99
else
  ./build.sh Clr+Libs+Mono -c Release --ninja -ci -p:OfficialBuildId="$TIMESTAMP"-99
fi

# Build Checked JIT compilers (only Checked JITs are able to filter assembly for printing codegen)
./build.sh Clr.AllJits -c Checked --ninja

cd src/tests

# Generate CORE_ROOT for Release
./build.sh Release generatelayoutonly
cd ../..

echo "${VERSION_WITHOUT_V}+${commit}" > "${CORE_ROOT}"/version.txt

# Copy Checked JITs to CORE_ROOT
cp artifacts/bin/coreclr/"${OS}".x64.Checked/libclrjit*.so "${CORE_ROOT}"
cp artifacts/bin/coreclr/"${OS}".x64.Checked/libclrjit*.so "${CORE_ROOT}"/crossgen2
if [ -d "${CORE_ROOT}"/ilc-published ]; then
    cp artifacts/bin/coreclr/"${OS}".x64.Checked/libclrjit*.so "${CORE_ROOT}"/ilc-published
fi

# Copy mono to CORE_ROOT_MONO
mkdir -p "${CORE_ROOT_MONO}"
cp -r "${CORE_ROOT}"/* "${CORE_ROOT_MONO}"
cp -r artifacts/bin/mono/"${OS}".x64.Release/* "${CORE_ROOT_MONO}"

# Move CORE_ROOT_MONO to CORE_ROOT/mono
mv "${CORE_ROOT_MONO}" "${CORE_ROOT}"/mono

# Runtime build is done, now build the DisassemblyLoader
cd "${DIR}"
./.dotnet/dotnet build -c Release ../../DisassemblyLoader/DisassemblyLoader.csproj -o "${CORE_ROOT}"/DisassemblyLoader

# Copy the bootstrapping .NET SDK, needed for 'dotnet build'
# Exclude the pdbs as when they are present, when running on Linux we get:
# Error: Image is either too small or contains an invalid byte offset or count.
# System.BadImageFormatException: Image is either too small or contains an invalid byte offset or count.
mv .dotnet/ "${CORE_ROOT}"/
cd "${CORE_ROOT}"/..
XZ_OPT=-2 tar Jcf "${OUTPUT}" --exclude \*.pdb --transform "s,^./,./dotnet-${VERSION}/," -C Core_Root .

echo "ce-build-status:OK"
