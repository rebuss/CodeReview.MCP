using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using REBUSS.Pure.AzureDevOps.Configuration;
using REBUSS.Pure.Cli;
using REBUSS.Pure.Core;
using REBUSS.Pure.DependencyInjection;
using REBUSS.Pure.GitHub.Configuration;
using REBUSS.Pure.Logging;
using REBUSS.Pure.Properties;
using REBUSS.Pure.ProviderDetection;

namespace REBUSS.Pure
{
    class Program
    {
        static async Task<int> Main(string[] args)
        {
            var parseResult = CliArgumentParser.Parse(args);
            if (!parseResult.IsServerMode)
                return await RunCliCommandAsync(parseResult);

            await RunMcpServerAsync(parseResult);
            return 0;
        }

        private static async Task<int> RunCliCommandAsync(CliParseResult parseResult)
        {
            ICliCommand command = parseResult.CommandName switch
            {
                var cmd when cmd == Resources.CliCommandInit => new InitCommand(
                    Console.Error,
                    Console.In,
                    Environment.CurrentDirectory,
                    GetExecutablePath(),
                    parseResult.Pat,
                    parseResult.IsGlobal,
                    parseResult.Ide,
                    parseResult.Agent,
                    detectedProvider: null,
                    processRunner: null,
                    localConfigStore: new LocalConfigStore(NullLogger<LocalConfigStore>.Instance),
                    gitHubConfigStore: new GitHubConfigStore(NullLogger<GitHubConfigStore>.Instance)),
                _ => throw new InvalidOperationException(string.Format(Resources.ErrorUnknownCommand, parseResult.CommandName))
            };

            return await command.ExecuteAsync();
        }

        private static async Task RunMcpServerAsync(CliParseResult parseResult)
        {
            try
            {
                var configuration = BuildConfiguration(parseResult);

                var builder = Host.CreateApplicationBuilder();

                // Replace the host's default configuration with our pre-built one
                builder.Services.AddSingleton<IConfiguration>(configuration);

                ConfigureLogging(builder.Logging, configuration);

                // Register business services (providers, algorithms, shared services)
                builder.Services.AddBusinessServices(configuration, parseResult.RepoPath, parseResult.Agent);

                // Add MCP server with stdio transport and tool discovery
                builder.Services
                    .AddMcpServer(options =>
                    {
                        options.ServerInfo = new() { Name = AppConstants.ServerName, Version = Resources.ServerVersion };
                    })
                    .WithStdioServerTransport()
                    .WithToolsFromAssembly();

                using var cts = new CancellationTokenSource();
                Console.CancelKeyPress += (_, e) =>
                {
                    e.Cancel = true;
                    cts.Cancel();
                };

                var app = builder.Build();

                // Apply CLI --repo argument if provided
                if (!string.IsNullOrWhiteSpace(parseResult.RepoPath))
                {
                    var workspaceRootProvider = app.Services.GetRequiredService<IWorkspaceRootProvider>();
                    workspaceRootProvider.SetCliRepositoryPath(parseResult.RepoPath);
                }

                await app.RunAsync(cts.Token);
            }
            catch (Exception ex)
            {
                await Console.Error.WriteLineAsync(string.Format(Resources.ErrorFatalMessage, AppConstants.ServerName, ex.GetType().FullName, ex.Message));
                if (ex.InnerException is not null)
                    await Console.Error.WriteLineAsync(string.Format(Resources.ErrorInnerMessage, AppConstants.ServerName, ex.InnerException.GetType().FullName, ex.InnerException.Message));
                await Console.Error.WriteLineAsync(ex.StackTrace ?? string.Empty);
                Environment.Exit(1);
            }
        }

        private static IConfiguration BuildConfiguration(CliParseResult parseResult)
        {
            var cliOverrides = CliConfigurationBuilder.BuildOverrides(parseResult);

            return new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile(Resources.AppSettingsFileName, optional: true, reloadOnChange: true)
                .AddJsonFile(Resources.AppSettingsLocalFileName, optional: true, reloadOnChange: true)
                .AddEnvironmentVariables()
                .AddInMemoryCollection(cliOverrides)
                .Build();
        }

        private static void ConfigureLogging(ILoggingBuilder logging, IConfiguration configuration)
        {
            // Configure logging: all output to stderr (Constitution Principle II).
            // The `Logging:LogLevel` rules from appsettings.json must be wired to our
            // explicit configuration — Host.CreateApplicationBuilder() reads from the
            // process working directory which, for MCP servers spawned by an IDE, is not
            // the executable directory. Without this line our LogLevel filters silently
            // never apply and every category logs at Information.
            logging.ClearProviders();
            logging.AddConfiguration(configuration.GetSection("Logging"));
            logging.AddConsole(options =>
            {
                options.LogToStandardErrorThreshold = LogLevel.Trace;
            });
            logging.AddProvider(new FileLoggerProvider(GetLogDirectory()));
        }

        private static string GetExecutablePath()
        {
            var processPath = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(processPath))
                return processPath;

            return Path.Combine(AppContext.BaseDirectory, AppConstants.ExecutableName);
        }

        private static string GetLogDirectory()
        {
            var logDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                AppConstants.ServerName);
            Directory.CreateDirectory(logDir);
            return logDir;
        }

        /// <summary>
        /// Back-compat shim for tests that still reference <c>Program.DetectProvider</c>.
        /// New code should call <see cref="ProviderDetector.Detect"/> directly.
        /// </summary>
        internal static string DetectProvider(IConfiguration configuration, string? repoPath = null)
            => ProviderDetector.Detect(configuration, repoPath);
    }
}
