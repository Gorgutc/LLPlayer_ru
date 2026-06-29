using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using LLPlayer.Views;
using Forms = System.Windows.Forms;

namespace LLPlayer.Extensions;

public partial class MyDialogWindow : Window, IDialogWindow
{
    public IDialogResult? Result { get; set; }

    public MyDialogWindow()
    {
        InitializeComponent();

        ApplyWorkAreaBounds();
        Loaded += (_, _) => ApplyWorkAreaBounds();

        MainWindow.SetTitleBarDarkMode(this);
    }

    private void ApplyWorkAreaBounds()
    {
        nint referenceHandle = GetReferenceHandle();
        Forms.Screen screen = referenceHandle != 0
            ? Forms.Screen.FromHandle(referenceHandle)
            : Forms.Screen.PrimaryScreen ?? Forms.Screen.AllScreens[0];

        Rect workArea = ToDeviceIndependentRect(screen.WorkingArea, referenceHandle);
        MaxWidth = workArea.Width;
        MaxHeight = workArea.Height;
    }

    private nint GetReferenceHandle()
    {
        if (Owner is not null)
        {
            nint ownerHandle = new WindowInteropHelper(Owner).Handle;
            if (ownerHandle != 0)
            {
                return ownerHandle;
            }
        }

        return new WindowInteropHelper(this).Handle;
    }

    private Rect ToDeviceIndependentRect(System.Drawing.Rectangle rect, nint referenceHandle)
    {
        if (referenceHandle != 0)
        {
            HwndSource? source = HwndSource.FromHwnd(referenceHandle);
            if (source?.CompositionTarget is not null)
            {
                Matrix transform = source.CompositionTarget.TransformFromDevice;
                Point topLeft = transform.Transform(new Point(rect.Left, rect.Top));
                Point bottomRight = transform.Transform(new Point(rect.Right, rect.Bottom));
                return new Rect(topLeft, bottomRight);
            }
        }

        DpiScale dpi = VisualTreeHelper.GetDpi(this);
        return new Rect(0, 0, rect.Width / dpi.DpiScaleX, rect.Height / dpi.DpiScaleY);
    }
}
