FROM ubuntu:20.04

ARG DEBIAN_FRONTEND=noninteractive
RUN apt update -y -q && apt upgrade -y -q
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
    lldb \
    llvm \
    make \
    ninja-build \
    xz-utils
 
RUN echo "LC_ALL=en_US.UTF-8" >> /etc/environment && \
    echo "en_US.UTF-8 UTF-8" >> /etc/locale.gen && \
    echo "LANG=en_US.UTF-8" > /etc/locale.conf && \
    locale-gen en_US.UTF-8

COPY build /root

WORKDIR /root
