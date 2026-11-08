using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Lmbox.AIAgent.WebAgent
{
    // Small native boundary: the implementation owns AppKit/WebKit, never Agent state.
    internal sealed class DockedWebView : IDisposable
    {
        private IntPtr _handle;
        public DockedWebView(string url)
        {
            _handle = LMBoxPanel_Create(url);
            if (_handle == IntPtr.Zero) throw new InvalidOperationException("无法创建内嵌 Web 页面。");
        }
        public void SetRect(Rect screenRect) => LMBoxPanel_SetRect(_handle,
            screenRect.x, screenRect.y, screenRect.width, screenRect.height);
        public void Hide() { if (_handle != IntPtr.Zero) LMBoxPanel_Hide(_handle); }
        public void Dispose()
        {
            if (_handle == IntPtr.Zero) return;
            LMBoxPanel_Destroy(_handle);
            _handle = IntPtr.Zero;
        }
        [DllImport("LmboxWebPanel", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr LMBoxPanel_Create([MarshalAs(UnmanagedType.LPUTF8Str)] string url);
        [DllImport("LmboxWebPanel", CallingConvention = CallingConvention.Cdecl)]
        private static extern int LMBoxPanel_SetRect(IntPtr handle, double x, double y, double width, double height);
        [DllImport("LmboxWebPanel", CallingConvention = CallingConvention.Cdecl)]
        private static extern void LMBoxPanel_Hide(IntPtr handle);
        [DllImport("LmboxWebPanel", CallingConvention = CallingConvention.Cdecl)]
        private static extern void LMBoxPanel_Destroy(IntPtr handle);
    }
}
