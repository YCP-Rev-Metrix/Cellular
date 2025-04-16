using System.Net.Http.Json;

namespace Cellular;

public partial class APItestPage : ContentPage
{
    public APItestPage()
    {
        InitializeComponent();
    }

    public class TestResponse
    {
        public int TestInt { get; set; }
    }

    private async void OnLoadTestDataClicked(object sender, EventArgs e)
    {
        try
        {
            using HttpClient client = new();
            string url = "https://api.revmetrix.io/api/gets/Test";

            var result = await client.GetFromJsonAsync<TestResponse>(url);

            if (result != null)
                ResultLabel.Text = $"TestInt value: {result.TestInt}";
            else
                ResultLabel.Text = "Failed to load data.";
        }
        catch (Exception ex)
        {
            ResultLabel.Text = $"Error: {ex.Message}";
        }
    }
}
