﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using ChannelPrototype.Bots;
using ChannelPrototype.Controllers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Integration.AspNet.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ChannelPrototype
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddMvc().SetCompatibilityVersion(CompatibilityVersion.Version_2_1);

            var storage = new MemoryStorage();
            services.AddSingleton<IStorage>(storage);

            services.AddSingleton(new ConversationState(storage));

            // Create the Bot Framework Adapter with error handling enabled.
            services.AddSingleton<BotFrameworkHttpAdapter, AdapterWithErrorHandler>();

            // Create the bot as a transient. In this case the ASP Controller is expecting an IBot.
            services.AddTransient<IBot, SkillHostBot>();

            services.AddSingleton<SkillRegistry>(new SkillRegistry()
            {
                {
                    "EchoSkill", new SkillRegistration() { Id = "EchoSkill", AppId = "apppidforskill", ServiceUrl = "http://localhost:4000/api/messages" }
                },
            });
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IHostingEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            else
            {
                app.UseHsts();
            }

            app.UseDefaultFiles();
            app.UseStaticFiles();

            // app.UseHttpsRedirection();
            app.UseMvc();
        }
    }
}
