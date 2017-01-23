﻿using System;
using System.IO;

using Synapse.Core;
using Synapse.Core.Utilities;

namespace Synapse.ControllerService.Dal
{
    public partial class FileSystemDal : IControllerDal
    {
        static readonly string CurrentPath = $"{Path.GetDirectoryName( typeof( FileSystemDal ).Assembly.Location )}";

        string _planPath = null;
        string _histPath = null;

        public FileSystemDal()
        {
            _planPath = $"{CurrentPath}\\Plans\\";
            _histPath = $"{CurrentPath}\\History\\";

            EnsurePaths();

            ProcessPlansOnSingleton = false;
            ProcessActionsOnSingleton = true;
        }

        public FileSystemDal(string basePath, bool processPlansOnSingleton = false, bool processActionsOnSingleton = true) : this()
        {
            if( string.IsNullOrWhiteSpace( basePath ) )
                basePath = CurrentPath;

            _planPath = $"{basePath}\\Plans\\";
            _histPath = $"{basePath}\\History\\";

            EnsurePaths();

            ProcessPlansOnSingleton = processPlansOnSingleton;
            ProcessActionsOnSingleton = processActionsOnSingleton;
        }

        void EnsurePaths()
        {
            Directory.CreateDirectory( _planPath );
            Directory.CreateDirectory( _histPath );
        }


        public bool ProcessPlansOnSingleton { get; set; }
        public bool ProcessActionsOnSingleton { get; set; }


        public Plan GetPlan(string planUniqueName)
        {
            string planFile = $"{_planPath}{planUniqueName}.yaml";
            return YamlHelpers.DeserializeFile<Plan>( planFile );
        }

        public Plan GetPlanStatus(string planUniqueName, long planInstanceId)
        {
            //string planFile = $"{_histPath}{planUniqueName}_{planInstanceId}.yaml";
            //return YamlHelpers.DeserializeFile<Plan>( planFile );
            string file = File.ReadAllText( $"{_histPath}out.txt" );
            return YamlHelpers.Deserialize<Plan>( file );
        }

        public void UpdatePlanStatus(Plan plan)
        {
            PlanUpdateItem item = new PlanUpdateItem() { Plan = plan };

            if( ProcessPlansOnSingleton )
                PlanItemSingletonProcessor.Instance.Queue.Enqueue( item );
            else
                UpdatePlanStatus( item );
        }

        public void UpdatePlanStatus(PlanUpdateItem item)
        {
            try
            {
                //YamlHelpers.SerializeFile( $"{_histPath}{item.Plan.UniqueName}_{item.Plan.InstanceId}.yaml",
                //    item.Plan, emitDefaultValues: true );
                string file = YamlHelpers.Serialize( item.Plan, emitDefaultValues: true );
                File.WriteAllText( $"{_histPath}out.txt", file ); //$"{_histPath}{plan.UniqueName}_{plan.InstanceId}.yaml"
            }
            catch( Exception ex )
            {
                PlanItemSingletonProcessor.Instance.Exceptions.Enqueue( ex );

                if( item.RetryAttempts++ < 5 )
                    PlanItemSingletonProcessor.Instance.Queue.Enqueue( item );
                else
                    PlanItemSingletonProcessor.Instance.Fatal.Enqueue( ex );
            }
        }

        public void UpdatePlanActionStatus(string planUniqueName, long planInstanceId, ActionItem actionItem)
        {
            ActionUpdateItem item = new ActionUpdateItem()
            {
                PlanUniqueName = planUniqueName,
                PlanInstanceId = planInstanceId,
                ActionItem = actionItem
            };

            if( ProcessActionsOnSingleton )
                ActionItemSingletonProcessor.Instance.Queue.Enqueue( item );
            else
                UpdatePlanActionStatus( item );
        }

        public void UpdatePlanActionStatus(ActionUpdateItem item)
        {
            try
            {
                Plan plan = GetPlanStatus( item.PlanUniqueName, item.PlanInstanceId );
                bool ok = Utilities.FindActionAndReplace( plan.Actions, item.ActionItem );
                if( ok )
                {
                    try
                    {
                        string file = YamlHelpers.Serialize( plan, emitDefaultValues: true );
                        File.WriteAllText( $"{_histPath}out.txt", file ); //$"{_histPath}{plan.UniqueName}_{plan.InstanceId}.yaml"
                    }
                    catch( Exception ex )
                    {
                        throw;
                    }
                }
                else
                    throw new Exception( $"Could not find Plan.InstanceId = [{item.PlanInstanceId}], Action:{item.ActionItem.Name}.ParentInstanceId = [{item.ActionItem.ParentInstanceId}] in Plan outfile." );
            }
            catch( Exception ex )
            {
                ActionItemSingletonProcessor.Instance.Exceptions.Enqueue( ex );

                if( item.RetryAttempts++ < 5 )
                    ActionItemSingletonProcessor.Instance.Queue.Enqueue( item );
                else
                    ActionItemSingletonProcessor.Instance.Fatal.Enqueue( ex );
            }
        }
    }
}