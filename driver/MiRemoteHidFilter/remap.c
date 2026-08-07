#include "remap.h"

#define MI_REMOTE_KEYBOARD_REPORT_ID 0x01

// HID Usage Page 0x07 usages emitted by Xiaomi Bluetooth Voice Remote 2 Pro.
#define HID_USAGE_KEYBOARD_VOLUME_UP     0x80
#define HID_USAGE_KEYBOARD_VOLUME_DOWN   0x81
#define HID_USAGE_KEYBOARD_BACK          0xF1

// Standard keyboard usages understood by Windows kbdhid.sys.
#define HID_USAGE_KEYBOARD_F13           0x68
#define HID_USAGE_KEYBOARD_F14           0x69
#define HID_USAGE_KEYBOARD_F15           0x6A

BOOLEAN
MiRemoteRemapReport(
    _Inout_updates_bytes_(Length) PUCHAR Report,
    _In_ SIZE_T Length
    )
{
    if (Report == NULL || Length < 4 || Report[0] != MI_REMOTE_KEYBOARD_REPORT_ID) {
        return FALSE;
    }

    // Captured reports use the standard keyboard layout after the report ID:
    // byte 1 = modifiers, byte 2 = reserved, byte 3 = key usage.
    // HIDCLASS pads the transfer to InputReportByteLength (121); only byte 3
    // carries the remote's single pressed key.
    switch (Report[3]) {
    case HID_USAGE_KEYBOARD_VOLUME_UP:
        Report[3] = HID_USAGE_KEYBOARD_F13;
        return TRUE;
    case HID_USAGE_KEYBOARD_VOLUME_DOWN:
        Report[3] = HID_USAGE_KEYBOARD_F14;
        return TRUE;
    case HID_USAGE_KEYBOARD_BACK:
        Report[3] = HID_USAGE_KEYBOARD_F15;
        return TRUE;
    default:
        return FALSE;
    }
}
