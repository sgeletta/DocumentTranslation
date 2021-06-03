﻿using DocumentTranslationService.Core;
using McMaster.Extensions.CommandLineUtils;
using System;
using System.Linq;
using System.Timers;

namespace DocumentTranslation.CLI
{
    partial class Program
    {
        private static DocumentTranslationService.Core.DocumentTranslationService TranslationService;


        public static int Main(string[] args)
        {
            CommandLineApplication app = new();
            app.HelpOption(inherited: true);
            app.Name = "DOCTR";
            app.Description = "DOCTR: Translate documents with the Azure Translator service.";
            app.OnExecute(() =>
            {
                app.ShowHelp();
                return 1;
            });
            app.Command("clear", clearCmd =>
            {
                clearCmd.Description = "Delete containers potentially left over from previous failed runs, in the given storage account, which are older than a week.";
                clearCmd.OnExecuteAsync(async (cancellationToken) =>
                {
                    DocTransAppSettings settings = await AppSettingsSetter.Read();
                    DocumentTranslationService.Core.DocumentTranslationService documentTranslationService = new(settings.SubscriptionKey, settings.AzureResourceName, settings.ConnectionStrings.StorageConnectionString);
                    DocumentTranslationBusiness translationBusiness = new(documentTranslationService);
                    try
                    {
                        int deletedCount = await translationBusiness.ClearOldContainersAsync();
                        Console.WriteLine($"Number of old containers deleted: {deletedCount}.");
                    }
                    catch (DocumentTranslationService.Core.DocumentTranslationService.CredentialsException)
                    {
                        Console.WriteLine(Properties.Resources.msg_MissingCredentials);
                        return;
                    }
                });
            }
            );
            app.Command("translate", translateCmd =>
            {
                translateCmd.AddName("trans");
                translateCmd.AddName("x");
                translateCmd.Description = "Translate a document or all documents in a folder.";
                var sourceFiles = translateCmd.Argument("source", "Translate this document or folder")
                                              .IsRequired(true, "A source document or folder is required.");
                var targetFolder = translateCmd.Argument("target", "Translate to this folder. If omitted, target folder is <sourcefolder>.<language>.");
                var toLang = translateCmd.Option("-t|--to <LanguageCode>", "The language code of the language to translate to. Use 'doctr languages' to see the available languages.", CommandOptionType.MultipleValue)
                                         .IsRequired(true, "Specification of a language to translate to is required.");
                var fromLang = translateCmd.Option("-f|--from <LanguageCode>",
                                                   "Optional: The language code of the language to translate from. Use 'doctr languages' to see the available languages. If omitted, the language will be auto-detected.",
                                                   CommandOptionType.SingleOrNoValue);
                var key = translateCmd.Option("-k|--key <SubscriptionKey>",
                                              "Optional: The subscription key to use for this translation. Will not be saved in config settings. If omitted, will use the key of the configuration.",
                                              CommandOptionType.SingleValue);
                var cat = translateCmd.Option("-c|--category",
                                              "The Custom Translator category to use. Set to 'none' or 'n' to force use of no category.",
                                              CommandOptionType.SingleValue);
                var gls = translateCmd.Option("-g|--glossary",
                                              "Glossary file, files, or folder to use as glossary. Cannot be same as source.",
                                              CommandOptionType.MultipleValue);
                var nodelete = translateCmd.Option("--nodelete",
                                                   "Do not delete the container in the storage account. For debugging purposes only.",
                                                   CommandOptionType.NoValue);
                translateCmd.OnExecuteAsync(async (cancellationToken) =>
                {
                    DocTransAppSettings settings = await AppSettingsSetter.Read();
                    if (key.HasValue()) settings.SubscriptionKey = key.Value();
                    try { AppSettingsSetter.CheckSettings(settings); }
                    catch (ArgumentException e)
                    {
                        var savedcolor = Console.ForegroundColor;
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"Missing credential: {e.Message}");
                        Console.ForegroundColor = savedcolor;
                        Console.WriteLine("Use 'DOCTR config set' to set the credentials.");
                        return;
                    };
                    DocumentTranslationService.Core.DocumentTranslationService documentTranslationService = new(settings.SubscriptionKey, settings.AzureResourceName, settings.ConnectionStrings.StorageConnectionString);
                    TranslationService = documentTranslationService;
                    DocumentTranslationBusiness translationBusiness = new(documentTranslationService);
                    if (nodelete.HasValue()) translationBusiness.Nodelete = true;
                    if (cat.HasValue()) documentTranslationService.Category = cat.Value();
                    translationBusiness.OnStatusUpdate += TranslationBusiness_OnStatusUpdate;
                    translationBusiness.OnDownloadComplete += TranslationBusiness_OnDownloadComplete;
                    translationBusiness.OnFilesDiscarded += TranslationBusiness_OnFilesDiscarded;
                    translationBusiness.OnUploadComplete += TranslationBusiness_OnUploadComplete;
                    Timer timer = new(500) { AutoReset = true, Enabled = true };
                    timer.Elapsed += Timer_Elapsed;
                    Console.WriteLine($"Starting translation of {sourceFiles.Value} to {toLang.Value()}. Press Esc to cancel.");
                    string target = null;
                    if (!string.IsNullOrEmpty(targetFolder.Value)) target = targetFolder.Value;
                    try
                    {
                        await translationBusiness.RunAsync(filestotranslate: sourceFiles.Values, fromlanguage: fromLang.Value(), tolanguage: toLang.Value(), glossaryfiles: gls.Values, targetFolder: target);
                    }
                    catch (System.ArgumentNullException e)
                    {
                        Console.WriteLine(e.Message);
                        return;
                    }
                    catch (System.ArgumentException e)
                    {
                        Console.WriteLine(e.Message);
                    }
                    catch (ServiceErrorException e)
                    {
                        Console.WriteLine(e.Message);
                        return;
                    }
                    Console.WriteLine($"Target folder: {translationBusiness.TargetFolder}");
                    timer.Stop();
                    timer.Dispose();
                });
            });
            app.Command("config", configCmd =>
            {
                configCmd.Description = "Configuration settings.";
                configCmd.Command("test", configTestCmd =>
                {
                    configTestCmd.Description = "Provide test result whether the configuration settings are functional. PASS if no problem found.";
                    configTestCmd.OnExecuteAsync(async (cancellationToken) =>
                    {
                        DocTransAppSettings docTransAppSettings = await AppSettingsSetter.Read(null);
                        DocumentTranslationService.Core.DocumentTranslationService translationService = new(docTransAppSettings.SubscriptionKey, docTransAppSettings.AzureResourceName, docTransAppSettings.ConnectionStrings.StorageConnectionString);
                        try { await translationService.TryCredentials(); }
                        catch (DocumentTranslationService.Core.DocumentTranslationService.CredentialsException ex)
                        {
                            switch (ex.Message.ToLowerInvariant())
                            {
                                case "key":
                                    Console.WriteLine("FAIL: Invalid or missing subscription key.");
                                    break;
                                case "storage":
                                    Console.WriteLine("FAIL: Storage account not present or invalid storage connection string.");
                                    break;
                                case "name":
                                    Console.WriteLine("FAIL: Resource name not found.");
                                    break;
                                default:
                                    Console.WriteLine($"Exception: {ex.Message}");
                                    break;
                            }
                            return;
                        };
                        Console.WriteLine("PASS");
                        return;
                    });
                });
                configCmd.Command("set", configSetCmd =>
                {
                    var key = configSetCmd.Option("--key <AzureKey>", "Azure key for the Translator resource. 'clear' to remove.", CommandOptionType.SingleValue);
                    var storage = configSetCmd.Option("--storage <StorageConnectionString>", "Connection string copied from the Azure storage resource. 'clear' to remove.", CommandOptionType.SingleValue);
                    var name = configSetCmd.Option("--name <ResourceName>", "Name of the Translator resource matching the \"key\". 'clear' to remove.", CommandOptionType.SingleValue);
                    var exp = configSetCmd.Option("--experimental <true/false>", "Show experimental languages. 'clear' to remove.", CommandOptionType.SingleValue);
                    var cat = configSetCmd.Option("--category", "Set the Custom Translator category to use for translations. 'clear' to remove.", CommandOptionType.SingleValue);
                    configSetCmd.Description = "Set the values of configuration parameters. Required before using Document Translation.";
                    configSetCmd.OnExecuteAsync(async (cancellationToken) =>
                    {
                        if (!(key.HasValue() || storage.HasValue() || name.HasValue() || exp.HasValue() || cat.HasValue())) configSetCmd.ShowHelp();
                        DocTransAppSettings docTransAppSettings = await AppSettingsSetter.Read(null);
                        if (key.HasValue())
                        {
                            if (key.Value().ToLowerInvariant() == "clear") docTransAppSettings.SubscriptionKey = string.Empty;
                            else docTransAppSettings.SubscriptionKey = key.Value();
                            Console.WriteLine($"{app.Name}: Subscription key set.");
                        }
                        if (storage.HasValue())
                        {
                            if (docTransAppSettings.ConnectionStrings is null)
                            {
                                Connectionstrings connectionstrings = new();
                                docTransAppSettings.ConnectionStrings = connectionstrings;
                            }
                            docTransAppSettings.ConnectionStrings.StorageConnectionString = storage.Value();
                            Console.WriteLine($"{app.Name}: Storage Connection String set.");
                        }
                        if (name.HasValue())
                        {
                            if (name.Value().ToLowerInvariant() == "clear") docTransAppSettings.AzureResourceName = string.Empty;
                            else docTransAppSettings.AzureResourceName = name.Value();
                            Console.WriteLine($"{app.Name}: Azure resource name set.");
                        }
                        if (cat.HasValue())
                        {
                            if (cat.Value().ToLowerInvariant() == "clear") docTransAppSettings.Category = string.Empty;
                            else docTransAppSettings.Category = cat.Value();
                            Console.WriteLine($"{app.Name}: Custom Translator Category set.");
                        }
                        if (exp.HasValue())
                        {
                            switch (exp.Value().ToLowerInvariant()[0])
                            {
                                case 't':
                                case 'y':
                                    docTransAppSettings.ShowExperimental = true;
                                    break;
                                case 'n':
                                case 'f':
                                    docTransAppSettings.ShowExperimental = false;
                                    Console.WriteLine($"{app.Name}: Experimental languages enabled.");
                                    break;
                                default:
                                    Console.WriteLine($"{app.Name}: Experimental flag not recognized. Must be yes, no, true, or false.");
                                    break;
                            }
                        }
                        await AppSettingsSetter.WriteAsync(null, docTransAppSettings);
                        return 0;
                    });
                    configSetCmd.OnValidationError((i) =>
                    {
                        configSetCmd.ShowHelp();
                        return 1;
                    });
                });
                configCmd.Command("list", configListCmd =>
                {
                    configListCmd.Description = "List the configuration settings.";
                    configListCmd.OnExecuteAsync(async (cancellationToken) =>
                    {
                        DocTransAppSettings docTransAppSettings = new();
                        docTransAppSettings = await AppSettingsSetter.Read();
                        Console.WriteLine(AppSettingsSetter.GetJson(docTransAppSettings));
                        return 0;
                    });
                });
                configCmd.OnExecute(() =>
                {
                    configCmd.ShowHelp();
                    return 1;
                });
            });
            app.Command("languages", langCmd =>
                {
                    langCmd.AddName("langs");
                    langCmd.AddName("list");
                    langCmd.Description = "List the languages available for translation.";
                    langCmd.OnExecuteAsync(async (cancellationToken) =>
                    {
                        DocumentTranslationService.Core.DocumentTranslationService translationService = new(null, null, null);
                        await translationService.GetLanguagesAsync();
                        foreach (var language in translationService.Languages.OrderBy(x => x.Key))
                        {
                            Console.WriteLine($"{language.Value.LangCode}\t{language.Value.Name}");
                        }
                        return 0;
                    });
                });
            app.Command("formats", formatsCmd =>
            {
                formatsCmd.AddName("format");
                formatsCmd.Description = "List the translatable document formats.";
                formatsCmd.OnExecuteAsync(async (cancellationToken) =>
                {
                    DocTransAppSettings settings = new();
                    settings = await AppSettingsSetter.Read();
                    DocumentTranslationService.Core.DocumentTranslationService translationService = new(settings.SubscriptionKey, settings.AzureResourceName, settings.ConnectionStrings.StorageConnectionString);
                    try
                    {
                        var result = await translationService.GetDocumentFormatsAsync();
                        if (result is null)
                        {
                            Console.WriteLine(Properties.Resources.msg_MissingCredentials);
                            return;
                        }
                    }
                    catch (DocumentTranslationService.Core.DocumentTranslationService.CredentialsException)
                    {
                        Console.WriteLine(Properties.Resources.msg_MissingCredentials);
                        return;
                    }
                    catch (System.UriFormatException)
                    {
                        Console.WriteLine(Properties.Resources.msg_WrongResourceName);
                        return;
                    }
                    foreach (var format in translationService.FileFormats.value.OrderBy(x => x.format))
                    {
                        Console.Write($"{format.format}");
                        foreach (string ext in format.fileExtensions) Console.Write($"\t{ext}");
                        Console.WriteLine();
                    }
                });
            });
            app.Command("glossary", glosCmd =>
            {
                glosCmd.AddName("glossaryformats");
                glosCmd.Description = "List the usable glossary formats.";
                glosCmd.OnExecuteAsync(async (cancellationToken) =>
                {
                    DocTransAppSettings settings = new();
                    settings = await AppSettingsSetter.Read();
                    DocumentTranslationService.Core.DocumentTranslationService translationService = new(settings.SubscriptionKey, settings.AzureResourceName, settings.ConnectionStrings.StorageConnectionString);
                    try
                    {
                        var result = await translationService.GetGlossaryFormatsAsync();
                        if (result is null)
                        {
                            Console.WriteLine(Properties.Resources.msg_MissingCredentials);
                            return;
                        }
                    }
                    catch (DocumentTranslationService.Core.DocumentTranslationService.CredentialsException)
                    {
                        Console.WriteLine(Properties.Resources.msg_MissingCredentials);
                        return;
                    }
                    catch (System.UriFormatException)
                    {
                        Console.WriteLine(Properties.Resources.msg_WrongResourceName);
                        return;
                    }
                    foreach (var format in translationService.GlossaryFormats.value.OrderBy(x => x.format))
                    {
                        Console.Write($"{format.format}");
                        foreach (string ext in format.fileExtensions) Console.Write($"\t{ext}");
                        Console.WriteLine();
                    }
                });
            });


            int result = 0;
            try
            {
                result = app.Execute(args);
            }
            catch (CommandParsingException e)
            {
                Console.WriteLine(e.Message);
            }

            return result;
        }

        private static void TranslationBusiness_OnUploadComplete(object sender, (int count, long sizeInBytes) e)
        {
            Console.WriteLine($"Submitted: {e.count} documents, {e.sizeInBytes} bytes.");
        }

        private static async void Timer_Elapsed(object sender, ElapsedEventArgs e)
        {
            //check whether user pressed Escape.
            if (Console.KeyAvailable)
            {
                if (Console.ReadKey().Key == ConsoleKey.Escape)
                {
                    Console.WriteLine();
                    Console.WriteLine("Canceling...");
                    await TranslationService.CancelRunAsync();
                }
            }
        }

        private static void TranslationBusiness_OnFilesDiscarded(object sender, System.Collections.Generic.List<string> discardedFilenames)
        {
            Console.WriteLine("Following files were excluded due to a file type mismatch:");
            foreach (string filename in discardedFilenames) Console.WriteLine(filename);
        }

        private static void TranslationBusiness_OnDownloadComplete(object Sender, (int count, long sizeInBytes) e)
        {
            Console.WriteLine($"Translation complete: {e.count} documents, {e.sizeInBytes} bytes.");
        }

        private static void TranslationBusiness_OnStatusUpdate(object sender, StatusResponse e)
        {
            if (e.error is not null)
            {
                Console.WriteLine($"{Properties.Resources.msg_ServerMessage}{e.error.code}: {e.error.message}\t{e.error.innerError.code}: {e.error.innerError.message}");
            }
            else
            {
                var time = DateTime.Parse(e.lastActionDateTimeUtc);
                Console.WriteLine($"{time.TimeOfDay}\tStatus: {e.status}\tIn progress: {e.summary.inProgress}\tSuccess: {e.summary.success}\tFail: {e.summary.failed}\tCharged: {e.summary.totalCharacterCharged} chars");
            }
        }
    }
}
