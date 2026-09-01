using ActiveScanner.Models;
using ActiveScanner.ViewModels;
using System.Collections.Generic;
using System.Windows;

namespace ActiveScanner.Views
{
    public partial class StaleDescriptionGroupDialog : Window
    {
        public StaleDescriptionGroupDialogViewModel ViewModel { get; }

        public StaleDescriptionGroupDialog(
            IEnumerable<AdObjectInfo> selectedComputers,
            IEnumerable<string> existingComputerGroupNames,
            string suggestedName)
        {
            InitializeComponent();
            ViewModel = new StaleDescriptionGroupDialogViewModel(
                selectedComputers, existingComputerGroupNames, suggestedName);
            DataContext = ViewModel;
            Loaded += (_, _) => GroupNameTextBox.Focus();
        }
    }
}
