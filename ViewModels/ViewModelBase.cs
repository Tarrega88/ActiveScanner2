using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ActiveScanner.ViewModels
{
    /// <summary>
    /// Base class for all ViewModels
    /// </summary>
    public abstract partial class ViewModelBase : ObservableObject
    {
        [ObservableProperty]
        private bool _isBusy;

        [ObservableProperty]
        private string? _busyMessage;

        [ObservableProperty]
        private string? _errorMessage;

        [ObservableProperty]
        private bool _hasError;

        protected void SetBusy(string? message = null)
        {
            IsBusy = true;
            BusyMessage = message;
            ClearError();
        }

        protected void ClearBusy()
        {
            IsBusy = false;
            BusyMessage = null;
        }

        protected void SetError(string message)
        {
            ErrorMessage = message;
            HasError = true;
        }

        protected void ClearError()
        {
            ErrorMessage = null;
            HasError = false;
        }

        [RelayCommand]
        private void DismissError()
        {
            ClearError();
        }
    }
}
