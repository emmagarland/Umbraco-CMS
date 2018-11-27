﻿using System;
using System.Collections;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Umbraco.Core;
using Umbraco.Core.Configuration.UmbracoSettings;
using Umbraco.Core.Logging;
using Umbraco.Core.Sync;

namespace Umbraco.Web.Scheduling
{
    //TODO: No scheduled task (i.e. URL) would be secured, so if people are actually using these each task
    // would need to be a publicly available task (URL) which isn't really very good :(
    // We should really be using the AdminTokenAuthorizeAttribute for this stuff

    internal class ScheduledTasks : RecurringTaskBase
    {
        private static HttpClient _httpClient;
        private readonly IRuntimeState _runtime;
        private readonly IUmbracoSettingsSection _settings;
        private readonly ILogger _logger;
        private readonly IProfilingLogger _proflog;
        private static readonly Hashtable ScheduledTaskTimes = new Hashtable();

        public ScheduledTasks(IBackgroundTaskRunner<RecurringTaskBase> runner, int delayMilliseconds, int periodMilliseconds,
            IRuntimeState runtime, IUmbracoSettingsSection settings, ILogger logger, IProfilingLogger proflog)
            : base(runner, delayMilliseconds, periodMilliseconds)
        {
            _runtime = runtime;
            _settings = settings;
            _logger = logger;
            _proflog = proflog;
        }

        private async Task ProcessTasksAsync(CancellationToken token)
        {
            var scheduledTasks = _settings.ScheduledTasks.Tasks;
            foreach (var t in scheduledTasks)
            {
                var runTask = false;
                if (ScheduledTaskTimes.ContainsKey(t.Alias) == false)
                {
                    runTask = true;
                    ScheduledTaskTimes.Add(t.Alias, DateTime.Now);
                }

                // Add 1 second to timespan to compensate for differencies in timer
                else if (
                    new TimeSpan(
                        DateTime.Now.Ticks - ((DateTime)ScheduledTaskTimes[t.Alias]).Ticks).TotalSeconds + 1 >= t.Interval)
                {
                    runTask = true;
                    ScheduledTaskTimes[t.Alias] = DateTime.Now;
                }

                if (runTask)
                {
                    var taskResult = await GetTaskByHttpAync(t.Url, token);
                    if (t.Log)
                        _logger.Info<ScheduledTasks>("{TaskAlias} has been called with response: {TaskResult}", t.Alias, taskResult);
                }
            }
        }

        private async Task<bool> GetTaskByHttpAync(string url, CancellationToken token)
        {
            if (_httpClient == null)
                _httpClient = new HttpClient
                {
                    BaseAddress = _runtime.ApplicationUrl
                };

            var request = new HttpRequestMessage(HttpMethod.Get, url);

            //TODO: pass custom the authorization header, currently these aren't really secured!
            //request.Headers.Authorization = AdminTokenAuthorizeAttribute.GetAuthenticationHeaderValue(_appContext);

            try
            {
                var result = await _httpClient.SendAsync(request, token).ConfigureAwait(false); // ConfigureAwait(false) is recommended? http://blog.stephencleary.com/2012/07/dont-block-on-async-code.html
                return result.StatusCode == HttpStatusCode.OK;
            }
            catch (Exception ex)
            {
                    _logger.Error<ScheduledTasks>(ex, "An error occurred calling web task for url: {Url}", url);

            }
            return false;
        }

        public override async Task<bool> PerformRunAsync(CancellationToken token)
        {
            switch (_runtime.ServerRole)
            {
                case ServerRole.Replica:
                    _logger.Debug<ScheduledTasks>("Does not run on replica servers.");
                    return true; // DO repeat, server role can change
                case ServerRole.Unknown:
                    _logger.Debug<ScheduledTasks>("Does not run on servers with unknown role.");
                    return true; // DO repeat, server role can change
            }

            // ensure we do not run if not main domain, but do NOT lock it
            if (_runtime.IsMainDom == false)
            {
                _logger.Debug<ScheduledTasks>("Does not run if not MainDom.");
                return false; // do NOT repeat, going down
            }

            using (_proflog.DebugDuration<ScheduledTasks>("Scheduled tasks executing", "Scheduled tasks complete"))
            {
                try
                {
                    await ProcessTasksAsync(token);
                }
                catch (Exception ex)
                {
                    _logger.Error<ScheduledTasks>(ex, "Error executing scheduled task");
                }
            }

            return true; // repeat
        }

        public override bool IsAsync => true;
    }
}
