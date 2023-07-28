FROM ubuntu:20.04

ARG DEBIAN_FRONTEND=noninteractive
RUN apt update -y -q && apt upgrade -y -q
RUN apt install -y -q \
    build-essential \
    clang \
    curl \
    gcc \
    gettext \
    git \
    language-pack-en \
    language-pack-en-base \
    libicu-dev \
    libkrb5-dev \
    liblttng-ust-dev \
    libnuma-dev \
    libssl-dev \
    libunwind8 \
    libunwind8-dev \
    lld \
    lldb \
    llvm \
    make \
    ninja-build \
    xz-utils \
    zlib1g-dev

RUN curl -sL https://github.com/Kitware/CMake/releases/download/v3.27.1/cmake-3.27.1-Linux-x86_64.tar.gz |\
    tar zxvf - -C /usr --strip-components=1

RUN echo "LC_ALL=en_US.UTF-8" >> /etc/environment && \
    echo "en_US.UTF-8 UTF-8" >> /etc/locale.gen && \
    echo "LANG=en_US.UTF-8" > /etc/locale.conf && \
    locale-gen en_US.UTF-8

COPY build /root

WORKDIR /root
