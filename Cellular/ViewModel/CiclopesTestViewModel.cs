using Cellular.Cloud_API;
using Cellular.Cloud_API.Models;

namespace Cellular.ViewModel;

public class CiclopesTestViewModel
{
    public async Task<CiclopesRunResponse?> RunTestAsync()
    {
        var controller = new ApiController();
        var request = new CiclopesRunRequest
        {
            VideoKey = "videos/310fceda-dac8-4bf0-a25c-2d1ba360ea68_shot1.mp4",
            SdKey = "key"
        };

        return await controller.ExecuteCiclopesRunRequest(request);
    }
}
