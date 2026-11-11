/* (hookshot) fopen for the files the game writes by itself in its folder and the user folder (hookshot_options.ini, the
   pipeline caches and lists, the button-prompt textures, mouse_sens.txt, language.txt and the like), without following a
   symbolic link at the file's name. A game folder unpacked from an archive someone shared can hold such a link (tar and
   unzip keep them on Linux): a plain fopen("w") then emptied and rewrote the file it named, anywhere the player can write.
   Linux: O_NOFOLLOW, and only a regular file (not a FIFO or a device). Windows: as fopen (an archive's links are not
   made there without a privilege). mode: "w", "wb", "a" or "ab". Used by the host (C) and the renderer (C++). */
#pragma once
#include <stdio.h>
#ifndef _WIN32
#include <fcntl.h>
#include <sys/stat.h>
#include <unistd.h>
#endif

static inline FILE *hos_fopen_write(const char *path, const char *mode) {
#ifdef _WIN32
    return fopen(path, mode);
#else
    const int append = mode[0] == 'a';
    if (!append && mode[0] != 'w') return NULL;
    const int fd = open(path, O_WRONLY | O_CREAT | O_CLOEXEC | O_NOFOLLOW | O_NONBLOCK | (append ? O_APPEND : 0), 0644);
    if (fd < 0) return NULL;
    struct stat st;
    /* (truncated only once it is known to be a regular file of its own name) */
    if (fstat(fd, &st) != 0 || !S_ISREG(st.st_mode) || (!append && ftruncate(fd, 0) != 0)) {
        close(fd);
        return NULL;
    }
    const int flags = fcntl(fd, F_GETFL);
    if (flags != -1) fcntl(fd, F_SETFL, flags & ~O_NONBLOCK);
    FILE *f = fdopen(fd, mode);
    if (!f) close(fd);
    return f;
#endif
}
