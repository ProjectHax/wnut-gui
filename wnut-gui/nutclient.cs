using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;

namespace wnut_gui
{
    internal class nutclient
    {
        private TcpClient? client = null;

        public nutclient()
        {
        }

        public nutclient(string host, int port)
        {
            connect(host, port);
        }

        bool send(string data)
        {
            if (client != null)
            {
                try
                {
                    byte[] buf = new byte[8192];
                    int size = client.Client.Send(Encoding.UTF8.GetBytes(data + "\n"));
                    if (size <= 0)
                        throw (new Exception());
                    return true;
                }
                catch (Exception)
                {
                    disconnect();
                }
            }

            return false;
         }

        string recv()
        {
            if (client == null)
                return string.Empty;

            try
            {
                byte[] buf = new byte[8192];
                int size = client.Client.Receive(buf, SocketFlags.None);
                if (size <= 0)
                {
                    disconnect();
                    return string.Empty;
                }

                string result = Encoding.UTF8.GetString(buf, 0, size);

                if (result.StartsWith("BEGIN LIST"))
                {
                    result = string.Empty;

                    do
                    {
                        size = client.Client.Receive(buf, SocketFlags.None);
                        if (size <= 0)
                            throw (new Exception("Not connected"));
                        result += Encoding.UTF8.GetString(buf, 0, size);
                    } while (!result.Contains("END LIST"));

                    result = result.Substring(0, result.IndexOf("END LIST") - 1);
                }
                else
                {
                    result = result.Substring(0, result.Length - 1);
                }

                return result;
            }
            catch (Exception)
            {
                disconnect();
                return string.Empty;
            }
        }

        public bool connect(string host, int port)
        {
            disconnect();

            client = new TcpClient();
            try
            {
                if (!client.ConnectAsync(host, port).Wait(2000))
                    throw (new Exception());
            }
            catch (Exception)
            {
                client = null;
                return false;
            }

            return true;
        }

        public bool connected()
        {
            return (client != null && client.Connected);
        }

        public void disconnect()
        {
            if (client != null)
            {
                client.Close();
                client = null;
            }
        }

        public bool login(string username, string password)
        {
            send(string.Format("USERNAME {0}", username));
            bool ok = recv() == "OK";
            send(string.Format("PASSWORD {0}", password));
            return ok && recv() == "OK";
        }

        public string get(string ups, string variable)
        {
            send(string.Format("GET VAR {0} {1}", ups, variable));
            string result = recv();
            result = result.Substring(result.IndexOf("\"") + 1);
            return result.Remove(result.Length - 1, 1);
        }

        public Dictionary<string, string> list(string ups)
        {
            var data = new Dictionary<string, string>();
            send(string.Format("LIST VAR {0}", ups));

            string result = recv();
            var lines = result.Split("\n");
            foreach (var l in lines)
            {
                var split = l.Split(" ");
                if (split.Length >= 3)
                {
                    var r = l.Substring(l.IndexOf("\"") + 1);
                    data[split[2]] = r.Remove(r.Length - 1, 1);
                }
            }

            return data;
        }
    }
}