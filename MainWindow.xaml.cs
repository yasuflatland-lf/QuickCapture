using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Windows.Graphics;
using Windows.Graphics.Imaging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Drawing;
using System.Drawing.Imaging;

namespace QuickCapture
{
    public sealed partial class MainWindow : Window
    {
        // Win32 API 呼び出し
        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        // Ctrlキーのコード
        private const int VK_CONTROL = 0x11;

        // ESCキーのコード
        private const int VK_ESCAPE = 0x1B;

        private bool isCapturing = false;
        private CancellationTokenSource? cancellationTokenSource;
        private int captureCount = 0;
        private string outputPath = "screenshot";
        private string fileExt = "jpg";
        private bool showPreview = true;

        private void StopCapture()
        {
            // キャプチャループをキャンセル
            cancellationTokenSource?.Cancel();
            isCapturing = false;

            // UIの状態を更新
            StartButton.Content = "開始";
            StatusTextBlock.Text = "停止しました";
            PreviewImage.Visibility = Visibility.Collapsed;
        }

        // 以下のStartButton_Clickメソッドも参考までに追加（すでに実装済みならスキップしてください）
        private void StartButton_Click(object sender, RoutedEventArgs e)
        {
            if (isCapturing)
            {
                StopCapture();
                return;
            }

            // 設定の取得
            outputPath = OutputPathTextBox.Text;
            fileExt = (FileFormatComboBox.SelectedItem as ComboBoxItem)?.Content.ToString()?.ToLower() ?? "jpg";
            showPreview = ShowPreviewCheckBox.IsChecked ?? true;

            // 出力ディレクトリの作成
            try
            {
                Directory.CreateDirectory(outputPath);
                captureCount = Directory.GetFiles(outputPath, $"*.{fileExt}").Length;
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = $"エラー: {ex.Message}";
                return;
            }

            isCapturing = true;
            StartButton.Content = "停止";
            StatusTextBlock.Text = "キャプチャモード: Ctrlキーを押しながらドラッグ";

            // キャプチャ処理の開始
            cancellationTokenSource = new CancellationTokenSource();
            _ = CaptureLoopAsync(cancellationTokenSource.Token);
        }

        // キャプチャループの実装
        private async Task CaptureLoopAsync(CancellationToken cancellationToken)
        {
            int prevKeyState = 0x0000;
            Point? startPoint = null;
            Bitmap? prevImage = null;

            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    // CTRLキー状態取得
                    short keyState = GetAsyncKeyState(VK_CONTROL);

                    // ESCキー状態取得
                    if (GetAsyncKeyState(VK_ESCAPE) != 0)
                    {
                        this.DispatcherQueue.TryEnqueue(() => StopCapture());
                        break;
                    }

                    // マウス座標の取得
                    GetCursorPos(out var cursorPos);
                    var x = cursorPos.X;
                    var y = cursorPos.Y;

                    // CTRL 押し始め
                    if ((keyState & 0x8000) != 0 && prevKeyState == 0x0000)
                    {
                        startPoint = new Point(x, y);
                        prevKeyState = 0x8000;
                    }
                    // CTRL 離す
                    else if ((keyState & 0x8000) == 0 && prevKeyState == 0x8000)
                    {
                        // キャプチャ保存
                        if (prevImage != null)
                        {
                            await SaveCaptureAsync(prevImage);
                        }

                        // 変数リセット
                        startPoint = null;
                        prevKeyState = 0x0000;
                        prevImage = null;

                        this.DispatcherQueue.TryEnqueue(() =>
                        {
                            PreviewImage.Visibility = Visibility.Collapsed;
                        });
                    }

                    // 選択範囲計算
                    if (startPoint != null)
                    {
                        int selectWidth = x - startPoint.Value.X;
                        int selectHeight = y - startPoint.Value.Y;

                        int x1, y1, x2, y2;

                        if (selectWidth > 0)
                        {
                            x1 = startPoint.Value.X;
                            x2 = x1 + selectWidth;
                        }
                        else
                        {
                            x1 = startPoint.Value.X + selectWidth;
                            x2 = startPoint.Value.X;
                        }

                        if (selectHeight > 0)
                        {
                            y1 = startPoint.Value.Y;
                            y2 = y1 + selectHeight;
                        }
                        else
                        {
                            y1 = startPoint.Value.Y + selectHeight;
                            y2 = startPoint.Value.Y;
                        }

                        // 前フレームの選択範囲がある場合はプレビュー表示
                        if (prevImage != null && showPreview)
                        {
                            this.DispatcherQueue.TryEnqueue(async () =>
                            {
                                try
                                {
                                    PreviewImage.Visibility = Visibility.Visible;
                                    PreviewImage.Source = await ConvertBitmapToWriteableBitmapAsync(prevImage);
                                }
                                catch { }
                            });
                        }

                        // 選択範囲が0以上のサイズの場合、スクリーンショット取得
                        if (Math.Abs(selectWidth) > 0 && Math.Abs(selectHeight) > 0)
                        {
                            try
                            {
                                prevImage = CaptureScreenRegion(x1, y1, x2 - x1, y2 - y1);
                            }
                            catch (Exception ex)
                            {
                                this.DispatcherQueue.TryEnqueue(() =>
                                {
                                    StatusTextBlock.Text = $"キャプチャエラー: {ex.Message}";
                                });
                            }
                        }
                    }

                    await Task.Delay(50); // CPU負荷軽減のための遅延
                }
            }
            catch (OperationCanceledException)
            {
                // キャンセルされた場合
            }
            catch (Exception ex)
            {
                this.DispatcherQueue.TryEnqueue(() =>
                {
                    StatusTextBlock.Text = $"エラー: {ex.Message}";
                    StopCapture();
                });
            }
            finally
            {
                prevImage?.Dispose();
            }
        }

        // System.Drawingを使用したスクリーンキャプチャ
        private Bitmap CaptureScreenRegion(int x, int y, int width, int height)
        {
            // 領域の幅と高さが負にならないように調整
            if (width < 0)
            {
                x += width;
                width = -width;
            }
            if (height < 0)
            {
                y += height;
                height = -height;
            }

            // 境界チェック
            if (width <= 0 || height <= 0)
            {
                throw new ArgumentException("キャプチャ領域のサイズが無効です");
            }

            // 画面全体のサイズを取得
            int screenWidth = GetSystemMetrics(SM_CXSCREEN);
            int screenHeight = GetSystemMetrics(SM_CYSCREEN);

            // 画面の範囲内に収まるように調整
            if (x < 0) x = 0;
            if (y < 0) y = 0;
            if (x + width > screenWidth) width = screenWidth - x;
            if (y + height > screenHeight) height = screenHeight - y;

            // キャプチャを実行
            var bitmap = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bitmap))
            {
                g.CopyFromScreen(x, y, 0, 0, new System.Drawing.Size(width, height), CopyPixelOperation.SourceCopy);
            }
            return bitmap;
        }

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int nIndex);
        private const int SM_CXSCREEN = 0;
        private const int SM_CYSCREEN = 1;

        // BitmapからWriteableBitmapへの変換
        private async Task<WriteableBitmap> ConvertBitmapToWriteableBitmapAsync(Bitmap bitmap)
        {
            // BitmapをメモリストリームにPNG形式で保存
            using (var stream = new MemoryStream())
            {
                bitmap.Save(stream, ImageFormat.Png);
                stream.Position = 0;

                // UIスレッドでWriteableBitmapを作成
                var writeableBitmap = new WriteableBitmap(bitmap.Width, bitmap.Height);
                await writeableBitmap.SetSourceAsync(stream.AsRandomAccessStream());
                return writeableBitmap;
            }
        }

        // キャプチャの保存
        private async Task SaveCaptureAsync(Bitmap bitmap)
        {
            string filename = $"{outputPath}/{captureCount:D6}.{fileExt}";
            captureCount++;

            try
            {
                // ディレクトリが存在することを確認
                Directory.CreateDirectory(Path.GetDirectoryName(filename));

                // 適切なImageFormatを選択
                ImageFormat format = fileExt.ToLower() == "png" ? ImageFormat.Png : ImageFormat.Jpeg;

                // 画像を保存
                bitmap.Save(filename, format);

                this.DispatcherQueue.TryEnqueue(() =>
                {
                    StatusTextBlock.Text = $"保存しました: {filename}";
                });
            }
            catch (Exception ex)
            {
                this.DispatcherQueue.TryEnqueue(() =>
                {
                    StatusTextBlock.Text = $"ファイル保存エラー: {ex.Message}";
                });
            }
        }
    }
}