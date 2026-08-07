#pragma once

#include <ntddk.h>

// Rewrites only Xiaomi remote keyboard report ID 0x01.
// Returns TRUE when at least one usage was replaced.
BOOLEAN
MiRemoteRemapReport(
    _Inout_updates_bytes_(Length) PUCHAR Report,
    _In_ SIZE_T Length
    );
