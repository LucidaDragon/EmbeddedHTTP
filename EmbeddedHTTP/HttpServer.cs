using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;

namespace EmbeddedHTTP
{
    /// <summary>
    /// A fully managed HTTP server implementation.
    /// </summary>
    public class HttpServer
    {
        /// <summary>
        /// The maximum amount of time a connection is allowed to be open for, in milliseconds.
        /// </summary>
        public int MaxConnectionTime { get; set; } = 60000;
        /// <summary>
        /// Whether to output request and response data to the console.
        /// </summary>
        public bool DataLogging { get; set; } = true;
        /// <summary>
        /// Whether to output exceptions to the console.
        /// </summary>
        public bool ErrorLogging { get; set; } = true;

        private const string Ok = "HTTP/1.1 200 OK\r\n\r\n";
        private const string BadRequest = "HTTP/1.1 400 Bad Request\r\nContent-Length: 22\r\nContent-Type: text/plain\r\n\r\nError 400: Bad Request";
        private const string NotFound = "HTTP/1.1 404 Not Found\r\nContent-Length: 20\r\nContent-Type: text/plain\r\n\r\nError 404: Not Found";
        private const string InternalError = "HTTP/1.1 500 Internal Server Error\r\nContent-Length: 32\r\nContent-Type: text/plain\r\n\r\nError 500: Internal Server Error";

        private Dictionary<string, Service> Services = new Dictionary<string, Service>();
        private TcpListener Listener;

        /// <summary>
        /// Add a type with services to the server's local scope.
        /// </summary>
        /// <param name="services">The type search for services in.</param>
        /// <returns>An array of endpoints registered during the operation.</returns>
        public string[] AddService(Type services)
        {
            var instance = services.GetConstructor(Type.EmptyTypes).Invoke(null);
            var result = new List<string>();

            foreach (var method in services.GetMethods())
            {
                var attrib = method.GetCustomAttribute<ServiceAttribute>();

                if (!(attrib is null))
                {
                    if (method.ReturnType == (attrib.Type == ServiceType.Text ? typeof(string) : typeof(byte[])))
                    {
                        var path = attrib.Path.ToLower();
                        Services.Add(path, new Service { Attribute = attrib, Path = path, Method = method, Type = attrib.Type, Target = instance, MetaService = attrib.MetaService });
                        result.Add(path);
                    }
                }
            }

            return result.ToArray();
        }

        /// <summary>
        /// Add an endpoint and service to the server's local scope.
        /// </summary>
        /// <param name="path">The endpoint for the service.</param>
        /// <param name="service">The service to add.</param>
        public void AddService(string path, Service service)
        {
            Services.Add(path, service);
        }

        /// <summary>
        /// Gets the service associated with the specified endpoint within the server's local scope.
        /// </summary>
        /// <param name="path">The endpoint for the service.</param>
        /// <param name="service">The service associated with the endpoint.</param>
        /// <returns>If the service is within the server's local scope.</returns>
        public bool TryGetService(string path, out Service service)
        {
            return Services.TryGetValue(path, out service);
        }

        /// <summary>
        /// Removes the service associated with the specified endpoint from the server's local scope.
        /// </summary>
        /// <param name="path">The endpoint for the service.</param>
        /// <returns>If the service was removed successfully.</returns>
        public bool RemoveService(string path)
        {
            return Services.Remove(path);
        }

        /// <summary>
        /// Start the server on the specified port.
        /// </summary>
        /// <param name="port">The port to listen on.</param>
        public void Start(ushort port)
        {
            if (!(Listener is null)) return;

            Listener = new TcpListener(new IPEndPoint(IPAddress.Any, port));
            Listener.Server.ReceiveBufferSize = int.MaxValue;
            Listener.Start();
            Listener.BeginAcceptTcpClient(AcceptConnection, null);
        }

        /// <summary>
        /// Stop the server.
        /// </summary>
        public void Stop()
        {
            Listener.Stop();
        }

        private void AcceptConnection(IAsyncResult result)
        {
            TcpClient client;

            try
            {
                client = Listener.EndAcceptTcpClient(result);

                Listener.BeginAcceptTcpClient(AcceptConnection, null);
            }
            catch (Exception ex)
            {
                if (ErrorLogging) Console.Error.WriteLine(ex);
                return;
            }

            try
            {
                var connectionStart = Environment.TickCount;

                var network = client.GetStream();
                var stream = new MemoryStream();

                int times = MaxConnectionTime / 10;
                while (client.Available <= 0)
                {
                    Thread.Sleep(10);
                    times--;

                    if (times <= 0) break;
                }

                int lastAvail = 0;
                while (lastAvail != client.Available)
                {
                    lastAvail = client.Available;
                    Thread.Sleep(100);
                }

                var requestBytes = new byte[client.Available];
                client.Client.Receive(requestBytes);
                stream.Write(requestBytes, 0, requestBytes.Length);
                stream.Position = 0;

                var request = new HttpRequest();
                var requestHeaders = new Dictionary<string, string>();
                var requestHeaderName = string.Empty;
                var requestData = new List<byte>();
                var state = HttpReceiveState.Method;

                var buffer = new List<char>();
                var next = stream.ReadByte();
                var last = '\0';
                while (next >= 0 && (Environment.TickCount - connectionStart) < MaxConnectionTime)
                {
                    var chr = (char)next;

                    switch (state)
                    {
                        case HttpReceiveState.Method:
                            if (chr == ' ')
                            {
                                var methodStr = new string(buffer.ToArray()).ToUpper();

                                if (Enum.TryParse(methodStr, out HttpMethod method))
                                {
                                    request.Method = method;
                                    buffer.Clear();
                                    state = HttpReceiveState.Path;
                                }
                                else
                                {
                                    state = HttpReceiveState.Invalid;
                                }
                            }
                            else
                            {
                                buffer.Add(chr);
                            }
                            break;

                        case HttpReceiveState.Path:
                            if (chr == ' ')
                            {
                                var pathStr = new string(buffer.ToArray()).ToLower();
                                request.Path = pathStr;
                                buffer.Clear();
                                state = HttpReceiveState.Version;
                            }
                            else
                            {
                                buffer.Add(chr);
                            }
                            break;

                        case HttpReceiveState.Version:
                            if (chr == '\n' && last == '\r')
                            {
                                var version = new string(buffer.ToArray()).ToUpper();
                                request.Version = version;
                                buffer.Clear();
                                state = HttpReceiveState.HeaderName;
                            }
                            else if (chr == '\r') { }
                            else
                            {
                                buffer.Add(chr);
                            }
                            break;

                        case HttpReceiveState.HeaderName:
                            if (chr == ' ' && last == ':')
                            {
                                requestHeaderName = new string(buffer.ToArray());
                                request.Headers.Add(requestHeaderName, string.Empty);
                                buffer.Clear();
                                state = HttpReceiveState.HeaderValue;
                            }
                            else if (chr == ':') { }
                            else if (chr == '\n' && last == '\r')
                            {
                                state = HttpReceiveState.Content;
                            }
                            else if (chr == '\r') { }
                            else
                            {
                                buffer.Add(chr);
                            }
                            break;

                        case HttpReceiveState.HeaderValue:
                            if (chr == '\n' && last == '\r')
                            {
                                var value = new string(buffer.ToArray());
                                request.Headers[requestHeaderName] = value;
                                buffer.Clear();
                                state = HttpReceiveState.HeaderName;
                            }
                            else if (chr == '\r') { }
                            else
                            {
                                buffer.Add(chr);
                            }
                            break;

                        case HttpReceiveState.Content:
                            requestData.Add((byte)next);
                            break;

                        default:
                            break;
                    }

                    if (state == HttpReceiveState.Invalid) break;

                    last = (char)next;
                    next = stream.ReadByte();
                }
                request.Data = requestData.ToArray();

                byte[] returnData;
                if (state == HttpReceiveState.Invalid)
                {
                    returnData = Encoding.UTF8.GetBytes(BadRequest);
                }
                else
                {
                    returnData = ProcessRequest(request);
                }

                client.Client.Send(returnData);

                try
                {
                    client.Close();
                }
                catch { }

                if (DataLogging)
                {
                    Console.WriteLine(string.Empty);
                    Console.WriteLine(Encoding.UTF8.GetString(requestBytes));
                    Console.WriteLine(string.Empty);
                    Console.WriteLine(Encoding.UTF8.GetString(returnData));
                    Console.WriteLine(string.Empty);
                }
            }
            catch (Exception ex)
            {
                if (ErrorLogging) Console.Error.WriteLine(ex);

                try
                {
                    client.Close();
                }
                catch { }
            }
        }

        private byte[] ProcessRequest(HttpRequest request)
        {
            if (Services.TryGetValue(request.Path, out Service service))
            {
                return service.RunService(request.Data, Services.Values.ToArray());
            }
            else
            {
                return Encoding.UTF8.GetBytes(NotFound);
            }
        }

        private class HttpRequest
        {
            public HttpMethod Method { get; set; } = HttpMethod.GET;
            public string Path { get; set; } = string.Empty;
            public string Version { get; set; } = string.Empty;
            public Dictionary<string, string> Headers { get; set; } = new Dictionary<string, string>();
            public byte[] Data { get; set; } = new byte[0];
        }

        /// <summary>
        /// Metadata for a service endpoint.
        /// </summary>
        public class Service
        {
            /// <summary>
            /// The attribute the metadata was obtained from.
            /// </summary>
            public ServiceAttribute Attribute { get; set; }
            /// <summary>
            /// The endpoint for the service.
            /// </summary>
            public string Path { get; set; }
            /// <summary>
            /// The content type for the service.
            /// </summary>
            public ServiceType Type { get; set; }
            /// <summary>
            /// The service method.
            /// </summary>
            public MethodInfo Method { get; set; }
            /// <summary>
            /// The object containing the service.
            /// </summary>
            public object Target { get; set; }
            /// <summary>
            /// Whether to pass the service list to the service.
            /// </summary>
            public bool MetaService { get; set; }

            public byte[] RunService(byte[] data, Service[] services)
            {
                try
                {
                    switch (Type)
                    {
                        case ServiceType.Text:
                            var text = RunTextService(Encoding.UTF8.GetString(data), services);
                            if (text is null)
                            {
                                return Encoding.UTF8.GetBytes(BadRequest);
                            }
                            else
                            {
                                return Encoding.UTF8.GetBytes(Ok + text);
                            }

                        case ServiceType.Binary:
                            var binary = RunBinaryService(data, services);
                            if (binary is null)
                            {
                                return Encoding.UTF8.GetBytes(BadRequest);
                            }
                            else
                            {
                                return Encoding.UTF8.GetBytes(Ok).Concat(binary).ToArray();
                            }

                        default:
                            return Encoding.UTF8.GetBytes(BadRequest);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex);
                    return Encoding.UTF8.GetBytes(InternalError);
                }
            }

            private byte[] RunBinaryService(byte[] data, Service[] services)
            {
                return (byte[])Method.Invoke(Target, MetaService ? new object[] { data, services } : new[] { data });
            }

            private string RunTextService(string data, Service[] services)
            {
                return (string)Method.Invoke(Target, MetaService ? new object[] { data, services } : new[] { data });
            }
        }

        private enum HttpReceiveState
        {
            Method,
            Path,
            Version,
            HeaderName,
            HeaderValue,
            Content,
            Invalid
        }

        private enum HttpMethod
        {
            GET,
            POST
        }
    }
}
