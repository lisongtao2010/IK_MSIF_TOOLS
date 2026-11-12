using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using OpenCvSharp;
using PdfiumViewer;

namespace CadPartSplitter
{
    /// <summary>
    /// 使用 PdfiumViewer（内部同样是 PDFium）把 PDF 页面渲染成 OpenCV 的 Mat（BGR 8UC3）。
    ///
    /// 本文件是「合并后」的实现：
    ///   * 原 CadPartSplitter 工程用 Docnet.Core 渲染，而 Docnet.Core 与 PdfiumViewer
    ///     各自携带一个同名的原生库 pdfium.dll（来源不同、导出表不同），
    ///     放进同一个输出目录必然互相覆盖，只有一个能加载成功；
    ///   * 合并成一个 exe 后统一走 PdfiumViewer，pdfium.dll 只剩 PdfiumViewer 的那一份。
    ///
    /// 对外 API 与原来的 Docnet.Core 版本完全一致（<see cref="GetPageCount"/> /
    /// <see cref="RenderPage"/>），所以 PartSplitterService / PartDetector / BatchRunner
    /// 一行都不用改。
    ///
    /// 与 Docnet.Core 版本的差异（都属于修正）：
    ///   * PDFium 默认以透明背景渲染，透明像素若直接当黑色用会导致后续二值化整页全黑；
    ///     这里先画到一张白底 24bpp 位图上，保证「白纸黑线」；
    ///   * 位图有行对齐（stride）填充，按行拷贝到 Mat，不假定紧凑排布；
    ///   * 页码越界时给出和原来一样的中文提示。
    /// </summary>
    public static class PdfRenderer
    {
        /// <summary>PDF 原生坐标单位：1 pt = 1/72 英寸。</summary>
        private const double PdfPointsPerInch = 72.0;

        /// <summary>
        /// 取 PDF 文件的总页数，供页码校验与界面提示使用。
        /// </summary>
        /// <param name="pdfPath">PDF 文件完整路径。</param>
        public static int GetPageCount(string pdfPath)
        {
            if (!File.Exists(pdfPath))
                throw new FileNotFoundException("PDF 文件未找到", pdfPath);

            using (PdfDocument document = PdfDocument.Load(pdfPath))
            {
                return document.PageCount;
            }
        }

        /// <summary>
        /// 把 PDF 文件指定页渲染成 Mat（BGR 格式，调用者负责 Dispose）。
        /// </summary>
        /// <param name="pdfPath">PDF 文件完整路径。</param>
        /// <param name="pageIndex">页码（从 0 开始）。</param>
        /// <param name="dpi">渲染分辨率。PDF 原生单位为 pt（1pt = 1/72 inch）。300 DPI 推荐。</param>
        public static Mat RenderPage(string pdfPath, int pageIndex = 0, double dpi = 300.0)
        {
            if (!File.Exists(pdfPath))
                throw new FileNotFoundException("PDF 文件未找到", pdfPath);

            if (dpi <= 0)
                throw new ArgumentOutOfRangeException("dpi", "渲染分辨率必须大于 0。");

            using (PdfDocument document = PdfDocument.Load(pdfPath))
            {
                if (pageIndex < 0 || pageIndex >= document.PageCount)
                    throw new InvalidOperationException(
                        "页面索引超出范围（共 " + document.PageCount + " 页，页码 0 ~ "
                        + (document.PageCount - 1) + "），请确认页码是否存在。");

                // 页面尺寸单位是 pt（1/72 英寸），乘 dpi/72 得到像素尺寸
                SizeF pageSize = document.PageSizes[pageIndex];
                int width  = (int)Math.Round(pageSize.Width  * dpi / PdfPointsPerInch);
                int height = (int)Math.Round(pageSize.Height * dpi / PdfPointsPerInch);

                if (width <= 0 || height <= 0)
                    throw new InvalidOperationException(
                        "PDF 页面尺寸无效（" + width + "×" + height + "），请确认页码是否存在。");

                using (Image image = document.Render(
                           pageIndex, width, height, (float)dpi, (float)dpi, PdfRenderFlags.Annotations))
                {
                    return ToBgrMat(image);
                }
            }
        }

        /// <summary>
        /// 把 <see cref="Image"/> 合成到白底 24bpp 位图后转成 BGR 的 Mat（调用者负责 Dispose）。
        /// </summary>
        private static Mat ToBgrMat(Image image)
        {
            if (image == null)
                throw new ArgumentNullException("image");

            using (var canvas = new Bitmap(image.Width, image.Height, PixelFormat.Format24bppRgb))
            {
                using (Graphics graphics = Graphics.FromImage(canvas))
                {
                    // PDFium 的透明背景在这里落成白色纸面，避免后续二值化把整页当黑
                    graphics.Clear(Color.White);
                    graphics.DrawImageUnscaled(image, 0, 0);
                }

                Mat mat = new Mat(canvas.Height, canvas.Width, MatType.CV_8UC3);
                IntPtr matData = mat.Data;
                long matStep = mat.Step();

                BitmapData bits = canvas.LockBits(
                    new Rectangle(0, 0, canvas.Width, canvas.Height),
                    ImageLockMode.ReadOnly,
                    PixelFormat.Format24bppRgb);

                try
                {
                    int rowBytes = canvas.Width * 3;
                    byte[] row = new byte[rowBytes];

                    for (int y = 0; y < canvas.Height; y++)
                    {
                        Marshal.Copy(IntPtr.Add(bits.Scan0, y * bits.Stride), row, 0, rowBytes);
                        Marshal.Copy(row, 0, IntPtr.Add(matData, (int)(y * matStep)), rowBytes);
                    }
                }
                catch (Exception)
                {
                    mat.Dispose();
                    throw;
                }
                finally
                {
                    canvas.UnlockBits(bits);
                }

                return mat;
            }
        }
    }
}
