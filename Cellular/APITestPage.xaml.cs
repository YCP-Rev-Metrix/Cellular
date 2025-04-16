using System.Net.Http.Json;

namespace Cellular;

public partial class APItestPage : ContentPage
{
    public APItestPage()
    {
        InitializeComponent();
    }
    private async void OnLoadTestDataClicked(object sender, EventArgs e)
    {
        try
        {
            using HttpClient client = new();
            string url = "https://api.revmetrix.io/api/gets/Test";

            string response = await client.GetStringAsync(url);

            // Try to parse the response as an integer
            if (int.TryParse(response, out int value))
            {
                ResultLabel.Text = $"Returned value: {value}";
            }
            else
            {
                ResultLabel.Text = "Failed to parse the response.";
            }
        }
        catch (Exception ex)
        {
            ResultLabel.Text = $"Error: {ex.Message}";
        }
    }
}
