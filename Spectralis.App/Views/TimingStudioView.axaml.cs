using Avalonia.Controls;
using Avalonia.Input;
using Spectralis.App.ViewModels;

namespace Spectralis.App.Views
{
    public partial class TimingStudioView : Window
    {
        public TimingStudioView()
        {
            InitializeComponent();
        }

        public TimingStudioView(TimingStudioViewModel vm) : this()
        {
            DataContext = vm;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.Key == Key.Space && DataContext is TimingStudioViewModel vm)
            {
                vm.StampCurrentLineCommand.Execute(null);
                e.Handled = true;
            }
        }
    }
}
