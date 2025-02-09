using Abp.AspNetCore;
using Abp.AspNetCore.SignalR.Hubs;
using Abp.Castle.Logging.Log4Net;
using Abp.Extensions;
using Castle.Facilities.Logging;
using ElmahCore;
using ElmahCore.Mvc;
using GraphQL;
using GraphQL.NewtonsoftJson;
using Hangfire;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Models;
using Newtonsoft.Json;
using Shesha.Authorization;
using Shesha.Configuration;
using Shesha.DynamicEntities;
using Shesha.DynamicEntities.Swagger;
using Shesha.GraphQL;
using Shesha.Identity;
using Shesha.Scheduler.Extensions;
using Shesha.Swagger;
using System;
using System.IO;
using System.Reflection;
using Shesha.GraphQL.Middleware;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Swashbuckle.AspNetCore.SwaggerGen;
using Microsoft.Extensions.Options;
using Swashbuckle.AspNetCore.Swagger;
using Shesha.Specifications;
using Shesha.GraphQL.Swagger;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Shesha.Application;
using System.Linq;
using Shesha.Extensions;
using Shesha.Exceptions;
using Shesha.Scheduler.Hangfire;
using Swashbuckle.AspNetCore.SwaggerUI;
using System.Collections.Generic;
using System.Collections;

namespace Shesha.Web.Host.Startup
{
    public class Startup
    {
        private readonly IConfigurationRoot _appConfiguration;
        private readonly IWebHostEnvironment _hostEnvironment;

        public Startup(IWebHostEnvironment hostEnvironment, IHostingEnvironment env)
        {
            _appConfiguration = env.GetAppConfiguration();
            _hostEnvironment = hostEnvironment;
        }

        public IServiceProvider ConfigureServices(IServiceCollection services)
        {
            // Should be before AddMvcCore
            services.AddSingleton<IActionDescriptorChangeProvider>(SheshaActionDescriptorChangeProvider.Instance);
            services.AddSingleton(SheshaActionDescriptorChangeProvider.Instance);

            services.Configure<IISServerOptions>(options =>
            {
                options.AllowSynchronousIO = true;
            });

            services.AddElmah<XmlFileErrorLog>(options =>
            {
                options.Path = @"elmah";
                options.LogPath = Path.Combine(_hostEnvironment.ContentRootPath, "App_Data", "ElmahLogs");
                //options.CheckPermissionAction = context => context.User.Identity.IsAuthenticated; //note: looks like we have to use cookies for it
            });

            services.AddMvcCore(options =>
                {
                    options.SuppressInputFormatterBuffering = true;

                    options.EnableEndpointRouting = false;
                    options.Conventions.Add(new ApiExplorerGroupPerVersionConvention());

                    options.EnableDynamicDtoBinding();
                    options.AddDynamicAppServices(services);
                    options.Filters.AddService(typeof(SpecificationsActionFilter));
                    options.Filters.AddService(typeof(SheshaAuthorizationFilter));
                    options.Filters.AddService(typeof(SheshaExceptionFilter), order: 1);
                })
                .AddApiExplorer()
                .AddNewtonsoftJson(options =>
                {
                    options.UseCamelCasing(true);
                    options.SerializerSettings.DateParseHandling = DateParseHandling.DateTimeOffset;
                })
                .SetCompatibilityVersion(CompatibilityVersion.Version_3_0);

            IdentityRegistrar.Register(services);
            AuthConfigurer.Configure(services, _appConfiguration);

            services.AddSignalR();

            services.AddCors();

            AddApiVersioning(services);

            services.AddHttpContextAccessor();
            services.AddHangfire(config =>
            {
                config.UseSqlServerStorage(_appConfiguration.GetConnectionString("Default"));
            });
            services.AddHangfireServer();

            //services.AddScoped<SheshaSchema>();

            // add Shesha GraphQL
            services.AddSheshaGraphQL();

            // Add ABP and initialize 
            // Configure Abp and Dependency Injection
            return services.AddAbp<SheshaWebHostModule>(
                options =>
                {
                    // Configure Log4Net logging
                    options.IocManager.IocContainer.AddFacility<LoggingFacility>(f => f.UseAbpLog4Net().WithConfig("log4net.config"));
                    // configure plugins
                    //options.PlugInSources.AddFolder(Path.Combine(_hostingEnvironment.WebRootPath, "Plugins"), SearchOption.AllDirectories);
                }
            );
        }

        public void Configure(IApplicationBuilder app, IHostingEnvironment env, ILoggerFactory loggerFactory)
        {
            app.UseElmah();

            // note: already registered in the ABP
            AppContextHelper.Configure(app.ApplicationServices.GetRequiredService<IHttpContextAccessor>());

            // use NHibernate session per request
            //app.UseNHibernateSessionPerRequest();

            app.UseHangfireDashboard();

            app.UseConfigurationFramework();

            app.UseAbp(options => { options.UseAbpRequestLocalization = false; }); // Initializes ABP framework.

            // global cors policy
            app.UseCors(x => x
                .AllowAnyMethod()
                .AllowAnyHeader()
                .SetIsOriginAllowed(origin => true) // allow any origin
                .AllowCredentials()); // allow credentials

            app.UseStaticFiles();

            app.UseAuthentication();

            app.UseAbpRequestLocalization();

            app.UseRouting();
            app.UseAuthorization();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllerRoute(
                    name: "defaultWithArea",
                    pattern: "{area}/{controller=Home}/{action=Index}/{id?}");
                endpoints.MapControllerRoute(
                    name: "default",
                    pattern: "{controller=Home}/{action=Index}/{id?}");
                endpoints.MapHub<AbpCommonHub>("/signalr");
                endpoints.MapControllers();
                endpoints.MapSignalRHubs();
            });

            // Enable middleware to serve generated Swagger as a JSON endpoint
            app.UseSwagger();

            // Enable middleware to serve swagger-ui assets (HTML, JS, CSS etc.)
            app.UseSwaggerUI(options =>
            {
                //options.AddEndpointsPerService();
                options.SwaggerEndpoint("swagger/v1/swagger.json", "Shesha API V1");

                options.IndexStream = () => Assembly.GetExecutingAssembly()
                    .GetManifestResourceStream("Shesha.Web.Host.wwwroot.swagger.ui.index.html");
            }); // URL: /swagger

            app.UseHangfireDashboard("/hangfire", new DashboardOptions
            {
                Authorization = new [] {new HangfireAuthorizationFilter() }
            });

            app.UseMiddleware<GraphQLMiddleware>();

            /*
            app.UseGraphQL<SheshaSchema>(path: "/graphql/person");
            app.UseGraphQL<EmptySchema>(path: "/graphql/empty");
            */
            app.UseGraphQLPlayground(); //to explorer API navigate https://*DOMAIN*/ui/playground
        }

        private void AddApiVersioning(IServiceCollection services)
        {
            services.Replace(ServiceDescriptor.Singleton<IApiControllerSpecification, AbpAppServiceApiVersionSpecification>());
            services.Configure<OpenApiInfo>(_appConfiguration.GetSection(nameof(OpenApiInfo)));

            services.AddTransient<IConfigureOptions<SwaggerGenOptions>, ConfigureSwaggerOptions>();

            //Swagger - Enable this line and the related lines in Configure method to enable swagger UI
            services.AddSwaggerGen(options =>
            {
                options.DescribeAllParametersInCamelCase();
                options.IgnoreObsoleteActions();
                options.AddXmlDocuments();

                options.SchemaFilter<GraphQLSchemaFilter>();
                options.SchemaFilter<DynamicDtoSchemaFilter>();
                options.OperationFilter<SwaggerOperationFilter>();

                options.CustomSchemaIds(type => SwaggerHelper.GetSchemaId(type));

                options.CustomOperationIds(desc => desc.ActionDescriptor is ControllerActionDescriptor d
                    ? d.ControllerName.ToCamelCase() + d.ActionName.ToPascalCase()
                    : null);

                options.AddDocumentsPerService();

                // Define the BearerAuth scheme that's in use
                options.AddSecurityDefinition("bearerAuth", new OpenApiSecurityScheme()
                {
                    Description = "JWT Authorization header using the Bearer scheme. Example: \"Authorization: Bearer {token}\"",
                    Name = "Authorization",
                    In = ParameterLocation.Header,
                    Type = SecuritySchemeType.ApiKey
                });

                options.ResolveConflictingActions(apiDescriptions => 
                    apiDescriptions.FirstOrDefault()
                );
                //options.SchemaFilter<DynamicDtoSchemaFilter>();
            });
            services.Replace(ServiceDescriptor.Transient<ISwaggerProvider, CachingSwaggerProvider>());

            services.AddApiVersioning(options =>
            {
                options.AssumeDefaultVersionWhenUnspecified = true;
                options.DefaultApiVersion = ApiVersion.Default;
                options.ReportApiVersions = true;
            });

            services.AddVersionedApiExplorer(options =>
            {
                options.GroupNameFormat = "'v'VVV";
                options.SubstituteApiVersionInUrl = true;
            });
        }
    }
}
