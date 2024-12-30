#!/bin/bash

set -exu

VERSION=$1
OS=linux
TIMESTAMP="$(date +%Y%m%d)"
AOT_BUILD_NEEDED=1
ILSPYCMD_VERSION="latest"
CORELIB_ARCHITECTURES=("x86" "x64" "arm" "arm64")

if echo "${VERSION}" | grep -q 'trunk'; then
    VERSION=trunk-"$TIMESTAMP"
    BRANCH=main
    VERSION_WITHOUT_V="${VERSION}"
    ILSPYCMD_VERSION="preview"
else
    BRANCH="${VERSION}"
    VERSION_WITHOUT_V="${VERSION:1}"
    MAJOR_VERSION="${VERSION_WITHOUT_V%%.*}"
    if [[ "${MAJOR_VERSION}" -lt 8 ]]; then
        OS=Linux
        ILSPYCMD_VERSION="8.2.0.7535"
    fi
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

# Build CoreLib for each architecture
mkdir "${CORE_ROOT}"/corelib

for ARCH in "${CORELIB_ARCHITECTURES[@]}"; do
    cd "${DIR}"
    ./build.sh -s clr.corelib -arch "$ARCH" -c Release
    mkdir "${CORE_ROOT}"/corelib/"$ARCH"
    cp -r artifacts/bin/coreclr/"${OS}"."$ARCH".Release/IL/* "${CORE_ROOT}"/corelib/"$ARCH"
done

# Runtime build is done, now build the DisassemblyLoader
cd "${DIR}"
./.dotnet/dotnet build -c Release ../../DisassemblyLoader/DisassemblyLoader.csproj -o "${CORE_ROOT}"/DisassemblyLoader

# Install ilspycmd
if [[ "${ILSPYCMD_VERSION}" == "preview" ]]; then
    ./.dotnet/dotnet tool install --tool-path "${CORE_ROOT}"/dotnet-tools ilspycmd --prerelease --add-source https://api.nuget.org/v3/index.json
elif [[ "${ILSPYCMD_VERSION}" == "latest" ]]; then
    ./.dotnet/dotnet tool install --tool-path "${CORE_ROOT}"/dotnet-tools ilspycmd --add-source https://api.nuget.org/v3/index.json
else
    ./.dotnet/dotnet tool install --tool-path "${CORE_ROOT}"/dotnet-tools ilspycmd --version ${ILSPYCMD_VERSION} --add-source https://api.nuget.org/v3/index.json
fi

# Copy the bootstrapping .NET SDK, needed for 'dotnet build'
# Exclude the pdbs as when they are present, when running on Linux we get:
# Error: Image is either too small or contains an invalid byte offset or count.
# System.BadImageFormatException: Image is either too small or contains an invalid byte offset or count.
mv .dotnet/ "${CORE_ROOT}"/
cd "${CORE_ROOT}"/..
XZ_OPT=-2 tar Jcf "${OUTPUT}" --exclude \*.pdb --transform "s,^./,./dotnet-${VERSION}/," -C Core_Root .

echo "ce-build-status:OK"
