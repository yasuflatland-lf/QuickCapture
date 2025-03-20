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
        // Win32 API �Ăяo��
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

        // Ctrl�L�[�̃R�[�h
        private const int VK_CONTROL = 0x11;

        // ESC�L�[�̃R�[�h
        private const int VK_ESCAPE = 0x1B;

        private bool isCapturing = false;
        private CancellationTokenSource? cancellationTokenSource;
        private int captureCount = 0;
        private string outputPath = "screenshot";
        private string fileExt = "jpg";
        private bool showPreview = true;

        private void StopCapture()
        {
            // �L���v�`�����[�v���L�����Z��
            cancellationTokenSource?.Cancel();
            isCapturing = false;

            // UI�̏�Ԃ��X�V
            StartButton.Content = "�J�n";
            StatusTextBlock.Text = "��~���܂���";
            PreviewImage.Visibility = Visibility.Collapsed;
        }

        // �ȉ���StartButton_Click���\�b�h���Q�l�܂łɒǉ��i���łɎ����ς݂Ȃ�X�L�b�v���Ă��������j
        private void StartButton_Click(object sender, RoutedEventArgs e)
        {
            if (isCapturing)
            {
                StopCapture();
                return;
            }

            // �ݒ�̎擾
            outputPath = OutputPathTextBox.Text;
            fileExt = (FileFormatComboBox.SelectedItem as ComboBoxItem)?.Content.ToString()?.ToLower() ?? "jpg";
            showPreview = ShowPreviewCheckBox.IsChecked ?? true;

            // �o�̓f�B���N�g���̍쐬
            try
            {
                Directory.CreateDirectory(outputPath);
                captureCount = Directory.GetFiles(outputPath, $"*.{fileExt}").Length;
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = $"�G���[: {ex.Message}";
                return;
            }

            isCapturing = true;
            StartButton.Content = "��~";
            StatusTextBlock.Text = "�L���v�`�����[�h: Ctrl�L�[�������Ȃ���h���b�O";

            // �L���v�`�������̊J�n
            cancellationTokenSource = new CancellationTokenSource();
            _ = CaptureLoopAsync(cancellationTokenSource.Token);
        }

        // �L���v�`�����[�v�̎���
        private async Task CaptureLoopAsync(CancellationToken cancellationToken)
        {
            int prevKeyState = 0x0000;
            Point? startPoint = null;
            Bitmap? prevImage = null;

            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    // CTRL�L�[��Ԏ擾
                    short keyState = GetAsyncKeyState(VK_CONTROL);

                    // ESC�L�[��Ԏ擾
                    if (GetAsyncKeyState(VK_ESCAPE) != 0)
                    {
                        this.DispatcherQueue.TryEnqueue(() => StopCapture());
                        break;
                    }

                    // �}�E�X���W�̎擾
                    GetCursorPos(out var cursorPos);
                    var x = cursorPos.X;
                    var y = cursorPos.Y;

                    // CTRL �����n��
                    if ((keyState & 0x8000) != 0 && prevKeyState == 0x0000)
                    {
                        startPoint = new Point(x, y);
                        prevKeyState = 0x8000;
                    }
                    // CTRL ����
                    else if ((keyState & 0x8000) == 0 && prevKeyState == 0x8000)
                    {
                        // �L���v�`���ۑ�
                        if (prevImage != null)
                        {
                            await SaveCaptureAsync(prevImage);
                        }

                        // �ϐ����Z�b�g
                        startPoint = null;
                        prevKeyState = 0x0000;
                        prevImage = null;

                        this.DispatcherQueue.TryEnqueue(() =>
                        {
                            PreviewImage.Visibility = Visibility.Collapsed;
                        });
                    }

                    // �I��͈͌v�Z
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

                        // �O�t���[���̑I��͈͂�����ꍇ�̓v���r���[�\��
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

                        // �I��͈͂�0�ȏ�̃T�C�Y�̏ꍇ�A�X�N���[���V���b�g�擾
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
                                    StatusTextBlock.Text = $"�L���v�`���G���[: {ex.Message}";
                                });
                            }
                        }
                    }

                    await Task.Delay(50); // CPU���׌y���̂��߂̒x��
                }
            }
            catch (OperationCanceledException)
            {
                // �L�����Z�����ꂽ�ꍇ
            }
            catch (Exception ex)
            {
                this.DispatcherQueue.TryEnqueue(() =>
                {
                    StatusTextBlock.Text = $"�G���[: {ex.Message}";
                    StopCapture();
                });
            }
            finally
            {
                prevImage?.Dispose();
            }
        }

        // System.Drawing���g�p�����X�N���[���L���v�`��
        private Bitmap CaptureScreenRegion(int x, int y, int width, int height)
        {
            // �̈�̕��ƍ��������ɂȂ�Ȃ��悤�ɒ���
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

            // ���E�`�F�b�N
            if (width <= 0 || height <= 0)
            {
                throw new ArgumentException("�L���v�`���̈�̃T�C�Y�������ł�");
            }

            // ��ʑS�̂̃T�C�Y���擾
            int screenWidth = GetSystemMetrics(SM_CXSCREEN);
            int screenHeight = GetSystemMetrics(SM_CYSCREEN);

            // ��ʂ͈͓̔��Ɏ��܂�悤�ɒ���
            if (x < 0) x = 0;
            if (y < 0) y = 0;
            if (x + width > screenWidth) width = screenWidth - x;
            if (y + height > screenHeight) height = screenHeight - y;

            // �L���v�`�������s
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

        // Bitmap����WriteableBitmap�ւ̕ϊ�
        private async Task<WriteableBitmap> ConvertBitmapToWriteableBitmapAsync(Bitmap bitmap)
        {
            // Bitmap���������X�g���[����PNG�`���ŕۑ�
            using (var stream = new MemoryStream())
            {
                bitmap.Save(stream, ImageFormat.Png);
                stream.Position = 0;

                // UI�X���b�h��WriteableBitmap���쐬
                var writeableBitmap = new WriteableBitmap(bitmap.Width, bitmap.Height);
                await writeableBitmap.SetSourceAsync(stream.AsRandomAccessStream());
                return writeableBitmap;
            }
        }

        // �L���v�`���̕ۑ�
        private async Task SaveCaptureAsync(Bitmap bitmap)
        {
            string filename = $"{outputPath}/{captureCount:D6}.{fileExt}";
            captureCount++;

            try
            {
                // �f�B���N�g�������݂��邱�Ƃ��m�F
                Directory.CreateDirectory(Path.GetDirectoryName(filename));

                // �K�؂�ImageFormat��I��
                ImageFormat format = fileExt.ToLower() == "png" ? ImageFormat.Png : ImageFormat.Jpeg;

                // �摜��ۑ�
                bitmap.Save(filename, format);

                this.DispatcherQueue.TryEnqueue(() =>
                {
                    StatusTextBlock.Text = $"�ۑ����܂���: {filename}";
                });
            }
            catch (Exception ex)
            {
                this.DispatcherQueue.TryEnqueue(() =>
                {
                    StatusTextBlock.Text = $"�t�@�C���ۑ��G���[: {ex.Message}";
                });
            }
        }
    }
}