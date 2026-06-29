using Microsoft.Extensions.DependencyInjection;
using Pando.Board.Forms;
using Pando.Board.Services;

namespace Pando.Board;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();

        var services = new ServiceCollection();
        services.AddSingleton<VelocityService>();
        services.AddTransient<BoardForm>();
        var provider = services.BuildServiceProvider();

        Application.Run(provider.GetRequiredService<BoardForm>());
    }
}
