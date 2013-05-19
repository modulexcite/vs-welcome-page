﻿using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Kiwi.Markdown;
using Kiwi.Markdown.ContentProviders;
using Nancy;
using Nancy.ErrorHandling;
using Nancy.Responses.Negotiation;
using Nancy.ViewEngines;

namespace WelcomePage.WebApplication
{
    public class ErrorHandler //: IStatusCodeHandler
    {
        private readonly IViewFactory _viewFactory;

        public ErrorHandler(IViewFactory viewFactory)
        {
            _viewFactory = viewFactory;
        }

        public bool HandlesStatusCode(HttpStatusCode statusCode, NancyContext context)
        {
            if (statusCode == HttpStatusCode.InternalServerError)
                return true;

            return false;
        }

        public void Handle(HttpStatusCode statusCode, NancyContext context)
        {
            object errorObject;
            context.Items.TryGetValue(NancyEngine.ERROR_EXCEPTION, out errorObject);

            var exception = errorObject as Exception;
            if (exception != null)
            {
                if (exception is RequestExecutionException)
                    exception = exception.InnerException;

                Handle(exception, context);
            }
        }

        private void Handle(Exception exception, NancyContext context)
        {
            // TODO: Also DirectoryNotFoundException.
            var fileNotFoundException = exception as FileNotFoundException;
            if (fileNotFoundException != null)
            {
                var renderer = new DefaultViewRenderer(_viewFactory);
                var response = renderer.RenderView(context, "Errors/404",
                                    new {fileNotFoundException.FileName});

                context.Response = response;
                context.Response.StatusCode = HttpStatusCode.NotFound;
                return;
            }
            
            throw exception;
        }
    }

    public class HomeModule : NancyModule
    {
        private readonly string _rootDirectory;
        private readonly IMarkdownService _converter;

        public HomeModule()
        {
            // BUG: YSOD when the root directory doesn't exist...
            _rootDirectory = GetRootDirectory();
            var contentProvider = new FileContentProvider(_rootDirectory);
            _converter = new MarkdownService(contentProvider);

            Get["/"] = context =>
                {
                    var id = FindDefaultDocumentId(_rootDirectory);
                    return GetDocument(id);
                };

            Get["/{id*}"] = context =>
                {
                    string id = context.Id;
                    return GetDocument(id);
                };

            Get["/_About"] = context =>
                {
                    var processId = Process.GetCurrentProcess().Id;
                    var location = Assembly.GetExecutingAssembly().Location;
                    var informationalVersion =
                        Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>();
                    var version =
                        Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyFileVersionAttribute>();
                    var model =
                        new
                            {
                                ProcessId = processId,
                                Location = location,
                                RootDirectory = _rootDirectory,
                                Version =
                                    informationalVersion != null
                                        ? informationalVersion.InformationalVersion
                                        : version.Version
                            };
                    return View["About", model];
                };
        }

        private Negotiator GetDocument(string name)
        {
            var document = _converter.GetDocument(name);

            // Kiwi.Markdown (or MarkdownSharp) doesn't appear to support [[Links]], so we'll do that here:
            var content = Regex.Replace(document.Content, @"\[\[(.*?)\]\]",
                                        m => string.Format("<a href=\"/{0}\">{0}</a>", m.Groups[1].Value));

            var model = new
                {
                    document.Title,
                    Content = content
                };
            return View["Index", model];
        }

        private string FindDefaultDocumentId(string rootDirectory)
        {
            var options = new[]
                {
                    "Index",    // because
                    "Home",     // github wiki
                    "README"    // github project
                };

            foreach (var option in options)
            {
                var path = Path.Combine(rootDirectory, string.Format("{0}.md", option));
                if (File.Exists(path))
                    return option;
            }

            throw new DefaultDocumentNotFoundException(rootDirectory, options);
        }

        private static string GetRootDirectory()
        {
            var rootDirectory = ConfigurationManager.AppSettings["RootDirectory"];
            if (string.IsNullOrWhiteSpace(rootDirectory))
                throw new ConfigurationErrorsException("RootDirectory is not configured.");
            return rootDirectory;
        }
    }

    internal class DefaultDocumentNotFoundException : FileNotFoundException
    {
        public DefaultDocumentNotFoundException(string rootDirectory, IEnumerable<string> options)
            : base(CreateMessage(rootDirectory, options))
        {
        }

        private static string CreateMessage(string rootDirectory, IEnumerable<string> options)
        {
            return
                string.Format("Cannot find default document in root directory '{0}'. Considered {1}.",
                              rootDirectory, string.Join(", ", options.Select(o => "'" + o + "'")));
        }
    }
}