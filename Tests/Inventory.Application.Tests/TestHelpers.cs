using System.Net;
using Inventory.Application.Common.Interfaces;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Inventory.Application.Tests;

public class DummyHostEnvironment : IHostEnvironment
{
    public string EnvironmentName { get; set; } = Environments.Development;
    public string ApplicationName { get; set; } = "Inventory";
    public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
    public IFileProvider ContentRootFileProvider { get; set; } = null!;
}
