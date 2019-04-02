﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Net.Http.Headers;

using Synapse.Common;
using Synapse.Core;
using Synapse.Common.WebApi;
using Synapse.Core.Runtime;

namespace Synapse.Services
{
    public class PlanRuntimePod : IPlanRuntimeContainer
    {
        LogUtility _log = new LogUtility();
        DirectoryInfo _logRootPath = null;
        bool _wantsCancel = false;
        string _url = null;

        ControllerServiceHttpApiClient _controllerService = null;

        public PlanRuntimePod(Plan plan, bool isDryRun = false, Dictionary<string, string> dynamicParameters = null, long planInstanceId = 0, Uri referrer = null, AuthenticationHeaderValue authHeader = null)
        {
            #region setup ControllerServiceHttpApiClient
            _url = referrer != null ? $"{referrer.Scheme}://{referrer.Host}:{referrer.Port}/synapse/execute" : null;

            string referrerMsg = referrer == null ? "Referrer is [null]" : $"Referrer is [{referrer.AbsoluteUri}]";
            ServerGlobal.Logger.Info( referrerMsg );

            if( ServerGlobal.Config.Node.HasControllerUrl )
                _url = ServerGlobal.Config.Node.ControllerUrl;

            if ( !string.IsNullOrWhiteSpace( _url ) )
            {
                _controllerService = new ControllerServiceHttpApiClient( _url );
                if ( ServerGlobal.Config?.Controller?.NodeAuthenticationScheme != null )
                {
                    if ( ServerGlobal.Config.Controller.NodeAuthenticationScheme == System.Net.AuthenticationSchemes.Basic )
                        _controllerService.Options.Authentication = new BasicAuthentication( authHeader );
                }
                else if ( authHeader != null )
                {
                    if ( authHeader.Scheme.ToLower() == "basic" )
                        _controllerService.Options.Authentication = new BasicAuthentication( authHeader );
                }
            }
            else
                throw new Exception( "Could not initialize ControllerServiceHttpApiClient from Referrer or Config.Node.ControllerUrl." );

            #endregion


            Plan = plan;
            IsDryRun = isDryRun;
            DynamicParameters = dynamicParameters;
            PlanInstanceId = planInstanceId;

            InitializeLogger();

            Plan.Progress += Plan_Progress;
            Plan.LogMessage += Plan_LogMessage;
        }

        public Plan Plan { get; }
        public bool IsDryRun { get; }
        public Dictionary<string, string> DynamicParameters { get; }
        public long PlanInstanceId { get; }


        public void InitializeLogger()
        {
            string logFilePath = null;
            try
            {
                string logFileName = $"{PlanInstanceId}_{Plan.Name}";
                _logRootPath = Directory.CreateDirectory( ServerGlobal.Config.Node.GetResolvedAuditLogRootPath() );
                logFilePath = $"{_logRootPath.FullName}\\{logFileName}.log";
                _log.InitDynamicFileAppender( logFileName, logFileName, logFilePath, ServerGlobal.Config.Node.Log4NetConversionPattern, "all" );
            }
            catch( Exception ex )
            {
                throw new FileNotFoundException( $"Could not find/acceess log file: {logFilePath}, AuditLogRootPath: {ServerGlobal.Config.Node.AuditLogRootPath}", ex );
            }
        }

        public void Start(CancellationToken token, Action<IPlanRuntimeContainer> callback)
        {
            ServerGlobal.Logger.Info( $"Starting {PlanInstanceId}_{Plan.Name}, Responding to ControllerService: {_url}" );

            token.Register( () => CancelPlanExecution() );

            try
            {
                Plan.Start( DynamicParameters, IsDryRun );
            }
            catch( Exception ex )
            {
                ServerGlobal.Logger.Fatal( $"Exception in {PlanInstanceId}_{Plan.Name}.", ex );
            }

            ServerGlobal.Logger.Info( $"SerializeResultPlan: {ServerGlobal.Config.Node.SerializeResultPlan}, {_logRootPath.FullName}\\{PlanInstanceId}_{Plan.Name}.result.yaml" );
            if( ServerGlobal.Config.Node.SerializeResultPlan )
                File.WriteAllText( $"{_logRootPath.FullName}\\{PlanInstanceId}_{Plan.Name}.result.yaml", Plan.ResultPlan.ToYaml() );

            //send final message home
            _controllerService.SetPlanStatusAsync( Plan.Name, PlanInstanceId, Plan.ResultPlan );

			_log.CloseLog();

            callback?.Invoke( this );
        }

        private void CancelPlanExecution()
        {
            _wantsCancel = true;
        }

        private void Plan_Progress(object sender, HandlerProgressCancelEventArgs e)
        {
            if( _wantsCancel )
                e.Cancel = true;

            //send intermediate message home
            _controllerService.SetPlanActionStatusAsync( Plan.Name, PlanInstanceId, e.ToActionItem() );
        }

        private void Plan_LogMessage(object sender, LogMessageEventArgs e)
        {
            _log.Write( e.SerializeSimple() );
        }
    }
}