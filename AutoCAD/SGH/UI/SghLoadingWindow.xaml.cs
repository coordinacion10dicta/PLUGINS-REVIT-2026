using System.Windows;

namespace AutoCAD.SGH.UI
{
    public partial class SghLoadingWindow : Window
    {
        public SghLoadingWindow()
        {
            InitializeComponent();
        }

        public void SetStatus(string message)
        {
            if (TxtStatus != null)
            {
                Dispatcher.Invoke(() =>
                {
                    TxtStatus.Text = message;
                });
            }
        }
    }
}
