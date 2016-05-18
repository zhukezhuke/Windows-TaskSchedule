﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Xml.Linq;
using Quartz;
using Windows.TaskSchedule.Extends;

namespace Windows.TaskSchedule.Utility
{
    public class ScheduleFactory: DefaultLogger
    {   
        static readonly string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Configs", "Jobs.config");
        static XDocument doc = XDocument.Load(configPath);
        public readonly static string ServerName = doc.Element("Jobs").Attribute("serverName").Value;
        public readonly static string Description = doc.Element("Jobs").Attribute("description").Value;
        public readonly static string DisplayName = doc.Element("Jobs").Attribute("displayName").Value;
        static List<JobObject> jobs = new List<JobObject>();
        public void Start()
        {
            Logger.Debug("服务开始启动...");
                       
            try
            {
                jobs = GetJobs();
            }
            catch (Exception ex)
            {
                Logger.Error(ex);
                throw;
            }

            Task.Factory.StartNew(() =>
            {
                while (true)
                {
                    foreach (var job in jobs)
                    {
                        if (!job.Running)
                        {
                            RunJob(job);
                        }
                    }
                    System.Threading.Thread.Sleep(1);
                }
            }); 
            Logger.Debug(string.Format("共找到【{0}】个任务.",jobs.Count));
            Logger.Debug(string.Format("当前服务运行目录:【{0}】.", AppDomain.CurrentDomain.BaseDirectory));
            Logger.Debug("服务启动成功.");
        }

        public void Stop() {

            Logger.Debug("服务停止.");
        }

        #region Private Method
        /// <summary>
        /// 获取配置文件中所有的任务
        /// </summary>
        /// <returns></returns>
        private List<JobObject> GetJobs()
        {
            List<JobObject> result = new List<JobObject>();
            var jobs = doc.Element("Jobs").Elements("Job");
            foreach (var p in jobs)
            {
                JobObject job = new JobObject();
                if (p.Attributes().Any(o => o.Name.ToString() == "type") && p.Attributes().Any(o => o.Name.ToString() == "exePath"))
                {
                    throw new Exception("job中不能同时配制“type”与“exePath”");
                }
                if (p.Attributes().Any(o => o.Name.ToString() == "type"))
                {
                    job.JobType = JobTypeEnum.Assembly;
                    string assembly = p.Attribute("type").Value.Split(',')[1];
                    string className = p.Attribute("type").Value.Split(',')[0];
                    job.Instance = Assembly.Load(assembly).CreateInstance(className) as IJob;
                }
                else if (p.Attributes().Any(o => o.Name.ToString() == "exePath"))
                {
                    job.JobType = JobTypeEnum.Exe;
                    job.ExePath = p.Attribute("exePath").Value.Replace("${basedir}", AppDomain.CurrentDomain.BaseDirectory);
                    if (p.Attributes().Any(o => o.Name.ToString() == "arguments"))
                    {
                        job.Arguments = p.Attribute("arguments").Value;
                    }
                }

                job.Name = p.Attribute("name").Value;
                job.CornExpress = p.Attribute("cornExpress").Value;
                if (!CronExpression.IsValidExpression(job.CornExpress))
                {
                    throw new Exception(string.Format("corn表达式：{0}不正确。", job.CornExpress));
                }
                result.Add(job);
            }
            return result;
        }

        /// <summary>
        /// 执行任务
        /// </summary>
        /// <param name="job">要执行的任务</param>
        private void RunJob(JobObject job)
        {
            try
            {
                if (CornUtility.Trigger(job.CornExpress, DateTime.Parse(DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"))))
                {
                    if (!job.Running && !job.Triggering)
                    {
                        job.Triggering = true;
                        Task.Factory.StartNew(() =>
                        {
                            try
                            {
                                job.Running = true;
                                switch (job.JobType)
                                {
                                    case JobTypeEnum.Assembly:
                                        job.Instance.Init();
                                        job.Instance.Excute();
                                        break;
                                    case JobTypeEnum.Exe:
                                        using (var process = new System.Diagnostics.Process())
                                        {
                                            if (string.IsNullOrWhiteSpace(job.Arguments))
                                            {
                                                process.StartInfo = new System.Diagnostics.ProcessStartInfo(job.ExePath);
                                            }
                                            else
                                            {
                                                process.StartInfo = new System.Diagnostics.ProcessStartInfo(job.ExePath, job.Arguments);
                                            }
                                            process.Start();
                                            process.WaitForExit();
                                        }
                                        break;
                                }
                            }
                            finally { job.Running = false; }
                        });
                    }
                }
                else
                {
                    job.Triggering = false;
                }
            }
            catch (Exception ex) //不处理错误，防止日志爆长
            {
                try
                {
                    if (job.JobType == JobTypeEnum.Assembly)
                    {
                        job.Instance.OnError(ex);
                    }
                }
                catch { }              
            }
        }
        #endregion

    }
}
