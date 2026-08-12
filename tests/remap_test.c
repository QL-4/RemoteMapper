#include <stdio.h>
#include <string.h>

#include "../driver/MiRemoteHidFilter/remap.h"

static int failures;

static void ExpectRemap(UCHAR input, UCHAR expected, const char *name)
{
    UCHAR report[121];
    memset(report, 0, sizeof(report));
    report[0] = 0x01;
    report[3] = input;

    if (!MiRemoteRemapReport(report, sizeof(report))) {
        printf("FAIL %s: function reported no remap\n", name);
        failures++;
    }
    if (report[3] != expected) {
        printf("FAIL %s: expected usage 0x%02X, got 0x%02X\n", name, expected, report[3]);
        failures++;
    }
}

int main(void)
{
    /* Existing unrecognised keys. */
    ExpectRemap(0x80, 0x68, "volume up -> F13");
    ExpectRemap(0x81, 0x69, "volume down -> F14");
    ExpectRemap(0xF1, 0x6A, "back -> F15");

    /* Device-specific F-keys prevent collisions with physical keyboard keys. */
    ExpectRemap(0x4A, 0x6B, "home -> F16");
    ExpectRemap(0x65, 0x6C, "menu -> F17");
    ExpectRemap(0x35, 0x6D, "live -> F18");
    ExpectRemap(0x66, 0x6E, "power -> F19");
    ExpectRemap(0x3E, 0x6F, "voice F5 -> F20");

    {
        UCHAR wrongId[4] = { 0x06, 0x00, 0x00, 0x4A };
        if (MiRemoteRemapReport(wrongId, sizeof(wrongId))) {
            printf("FAIL vendor report ID must remain untouched\n");
            failures++;
        }
    }

    if (failures != 0) return 1;
    printf("PASS MiRemoteRemapReport isolates every mapped remote key\n");
    return 0;
}
