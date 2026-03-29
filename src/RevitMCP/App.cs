using Autodesk.Revit.UI;
using System;
using System.Net;
using System.Threading;
using System.Text;
using Newtonsoft.Json;
using System.Collections.Generic;

namespace RevitMCP
{
    [Autodesk.Revit.Attributes.Regeneration(Autodesk.Revit.Attributes.RegenerationOption.Manual)]
    public class App : IExternalApplication
    {
        public static HttpListener Listener;
        public static RevitCommandHandler CommandHandler;
        public static ExternalEvent RevitEvent;

        public Result OnStartup(UIControlledApplication app)
        {
            try
            {
                CommandHandler = new RevitCommandHandler();
                RevitEvent = ExternalEvent.Create(CommandHandler);

                Listener = new HttpListener();
                Listener.Prefixes.Add("http://localhost:8765/");
                Listener.Start();

                Thread listenerThread = new Thread(ListenLoop) { IsBackground = true };
                listenerThread.Start();

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                TaskDialog.Show("RevitMCP Error", ex.Message);
                return Result.Failed;
            }
        }

        public Result OnShutdown(UIControlledApplication app)
        {
            try { Listener?.Stop(); } catch { }
            return Result.Succeeded;
        }

        private void ListenLoop()
        {
            while (Listener.IsListening)
            {
                try
                {
                    var context = Listener.GetContext();
                    ThreadPool.QueueUserWorkItem(_ => HandleRequest(context));
                }
                catch { break; }
            }
        }

        private void HandleRequest(HttpListenerContext context)
        {
            try
            {
                string body = new System.IO.StreamReader(context.Request.InputStream).ReadToEnd();
                var request = JsonConvert.DeserializeObject<Dictionary<string, object>>(body);

                PendingCommand cmd = CommandHandler.EnqueueCommand(request);
                RevitEvent.Raise();
                bool completed = cmd.CompletedEvent.Wait(TimeSpan.FromSeconds(15));

                Dictionary<string, object> result;
                if (!completed)
                    result = new Dictionary<string, object> { ["error"] = "Timeout - open a Revit model and keep Revit in focus" };
                else
                    result = cmd.Result ?? new Dictionary<string, object> { ["error"] = "Null result" };

                byte[] buf = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(result));
                context.Response.ContentType = "application/json";
                context.Response.ContentLength64 = buf.Length;
                context.Response.OutputStream.Write(buf, 0, buf.Length);
                context.Response.OutputStream.Close();
            }
            catch (Exception ex)
            {
                try
                {
                    byte[] buf = Encoding.UTF8.GetBytes("{\"error\":\"" + ex.Message.Replace("\"", "'") + "\"}");
                    context.Response.ContentLength64 = buf.Length;
                    context.Response.OutputStream.Write(buf, 0, buf.Length);
                    context.Response.OutputStream.Close();
                }
                catch { }
            }
        }
    }
}
