using System;
using System.Runtime.InteropServices;

namespace CaptureEncoder
{

    // To populate an AudioFrame with audio data, 
    // you must get access to the underlying memory buffer 
    // of the audio frame.To do this you must initialize 
    // the IMemoryBufferByteAccess COM interface 
    // by adding the following code within your namespace.
    //
    [ComImport]
    [Guid("5B0D3235-4DBA-4D44-865E-8F1D0E4FD04D")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    unsafe interface IMemoryBufferByteAccess
    {
        void GetBuffer(out byte* buffer, out uint capacity);
    }
    // You must also configure your project in Microsoft Visual Studio 
    // to allow the compilation of unsafe code 
    // by opening the project's Properties page, 
    // clicking the Build property page, 
    // and selecting the Allow Unsafe Code checkbox
}
