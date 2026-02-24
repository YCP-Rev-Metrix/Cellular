using CommunityToolkit.Maui.Core;
using CommunityToolkit.Maui.Views;
using System.Threading.Tasks;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Cellular.Views
{
    public partial class CommentEditorPopup : Popup, INotifyPropertyChanged
    {
        private string _currentCommentText = string.Empty;

        public string CurrentCommentText
        {
            get => _currentCommentText;
            set
            {
                if (_currentCommentText != value)
                {
                    _currentCommentText = value;
                    OnPropertyChanged();
                }
            }
        }

        // TaskCompletionSource to provide a result to the caller when CloseAsync() is used.
        public TaskCompletionSource<string?> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public CommentEditorPopup(string initialText = "")
        {
            InitializeComponent();

            CurrentCommentText = initialText ?? "";
            BindingContext = this;
        }

        private async void OnSaveClicked(object sender, EventArgs e)
        {
            // Provide the edited comment to the caller
            Completion.TrySetResult(CurrentCommentText);
            await CloseAsync();
        }

        private async void OnCancelClicked(object sender, EventArgs e)
        {
            // Indicate cancellation by returning null
            Completion.TrySetResult(null);
            await CloseAsync();
        }
    }
}