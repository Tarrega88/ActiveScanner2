using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using ActiveScanner.Models;
using ActiveScanner.ViewModels;

namespace ActiveScanner.Views
{
    /// <summary>
    /// Dialog for creating or editing target groups
    /// </summary>
    public partial class GroupDialog : Window
    {
        private readonly GroupDialogViewModel _viewModel;

        public TargetGroup? ResultGroup { get; private set; }

        public GroupDialog(GroupDialogViewModel viewModel)
        {
            InitializeComponent();
            _viewModel = viewModel;
            DataContext = _viewModel;

            _viewModel.RequestClose += OnRequestClose;
        }

        private void OnRequestClose(object? sender, bool result)
        {
            if (result)
            {
                ResultGroup = _viewModel.CreateGroup();
            }
            DialogResult = result;
            Close();
        }

        private void SubfoldersList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_viewModel.SelectedFolder != null)
            {
                var folder = _viewModel.SelectedFolder;
                _viewModel.HandleFolderDoubleClick(folder);
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            _viewModel.RequestClose -= OnRequestClose;
            _viewModel.Dispose();
            base.OnClosed(e);
        }
    }

    /// <summary>
    /// Converts bool (IsEditMode) to dialog title
    /// </summary>
    public class BoolToTitleConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is bool isEdit && isEdit ? "Edit Group" : "Create Group";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Converts bool (IsEditMode) to save button text
    /// </summary>
    public class BoolToSaveButtonTextConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is bool isEdit && isEdit ? "Update Group" : "Create Group";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Converts GroupObjectType to display string
    /// </summary>
    public class ObjectTypeToStringConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is GroupObjectType type ? type switch
            {
                GroupObjectType.Computers => "Computers",
                GroupObjectType.Users => "Users",
                GroupObjectType.Printers => "Printers",
                _ => "Multi-Type"
            } : "Multi-Type";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
