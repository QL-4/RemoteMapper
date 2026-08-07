#pragma once

#include <ntddk.h>
#include <wdf.h>

DRIVER_INITIALIZE DriverEntry;
EVT_WDF_DRIVER_DEVICE_ADD MiRemoteEvtDeviceAdd;
EVT_WDF_IO_QUEUE_IO_READ MiRemoteEvtIoRead;
EVT_WDF_REQUEST_COMPLETION_ROUTINE MiRemoteReadCompletion;
