using System.Collections.Generic;
using System.Windows;
using ActiveScanner.ViewModels;
using ActiveScanner.Models;

namespace ActiveScanner.Views
{
    public partial class CustomGroupDialog : Window
    {
        public CustomGroupDialogViewModel ViewModel { get; }

        public CustomGroupDialog(CustomGroup group, IEnumerable<string>? otherGroupNames = null)
        {
            InitializeComponent();
            ViewModel = new CustomGroupDialogViewModel(group, otherGroupNames);
            DataContext = ViewModel;
        }
    }
}
