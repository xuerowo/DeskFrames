using System.Drawing.Imaging;

namespace DeskFrames;

/// <summary>
/// 以 DIB Section 直接給 GDI+ 繪圖、再用 UpdateLayeredWindow 顯示的逐像素透明畫布。
/// 非編輯狀態下畫完即可 Release()，由系統保留畫面，程式不占額外記憶體。
/// </summary>
public sealed class LayeredSurface : IDisposable
{
    private IntPtr _hdc, _hbmp, _old, _bits;
    private Bitmap? _bmp;
    private int _w, _h;

    public Graphics Begin(int w, int h)
    {
        w = Math.Max(1, w); h = Math.Max(1, h);
        if (w != _w || h != _h || _bmp == null) Allocate(w, h);
        var g = Graphics.FromImage(_bmp!);
        g.Clear(Color.Transparent);
        return g;
    }

    private void Allocate(int w, int h)
    {
        Release();
        var bi = new Native.BITMAPINFOHEADER
        {
            biSize = System.Runtime.InteropServices.Marshal.SizeOf<Native.BITMAPINFOHEADER>(),
            biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32,
        };
        _hbmp = Native.CreateDIBSection(IntPtr.Zero, ref bi, 0, out _bits, IntPtr.Zero, 0);
        if (_hbmp == IntPtr.Zero) throw new OutOfMemoryException("CreateDIBSection failed");
        _hdc = Native.CreateCompatibleDC(IntPtr.Zero);
        _old = Native.SelectObject(_hdc, _hbmp);
        _bmp = new Bitmap(w, h, w * 4, PixelFormat.Format32bppPArgb, _bits);
        _w = w; _h = h;
    }

    public void Commit(IntPtr hwnd, int x, int y)
    {
        if (_bmp == null) return;
        var dst = new Native.POINT(x, y);
        var size = new Native.SIZE(_w, _h);
        var src = new Native.POINT(0, 0);
        var blend = new Native.BLENDFUNCTION
        {
            BlendOp = Native.AC_SRC_OVER, SourceConstantAlpha = 255, AlphaFormat = Native.AC_SRC_ALPHA,
        };
        Native.UpdateLayeredWindow(hwnd, IntPtr.Zero, ref dst, ref size, _hdc, ref src, 0, ref blend, Native.ULW_ALPHA);
    }

    /// <summary>釋放畫布記憶體（視窗內容仍由系統保留）。</summary>
    public void Release()
    {
        _bmp?.Dispose(); _bmp = null;
        if (_hdc != IntPtr.Zero) { Native.SelectObject(_hdc, _old); Native.DeleteDC(_hdc); _hdc = IntPtr.Zero; }
        if (_hbmp != IntPtr.Zero) { Native.DeleteObject(_hbmp); _hbmp = IntPtr.Zero; }
        _w = _h = 0;
    }

    public void Dispose() => Release();
}
