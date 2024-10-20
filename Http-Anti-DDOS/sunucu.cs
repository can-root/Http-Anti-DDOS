using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Threading;

class Program
{
    private static HttpListener _dinleyici;
    private static int _maksBağlantıSayısı;
    private static TimeSpan _bağlantıPenceresi = TimeSpan.FromSeconds(1);
    private static ConcurrentDictionary<string, (int sayac, DateTime sonİstekZamani, int banSayisi, DateTime banSonu)> _ipBağlantıları = new ConcurrentDictionary<string, (int, DateTime, int, DateTime)>();
    private static readonly object _kilit = new object();

    static void Main(string[] args)
    {
        Console.Write("IP adresini girin: ");
        string ipAdresi = Console.ReadLine();

        Console.Write("Port numarasını girin: ");
        int port = int.Parse(Console.ReadLine());

        Console.Write("Saniyede kabul edilecek maksimum bağlantı sayısını girin: ");
        _maksBağlantıSayısı = int.Parse(Console.ReadLine());

        SunucuBaşlat(ipAdresi, port);
    }

    static void SunucuBaşlat(string ipAdresi, int port)
    {
        _dinleyici = new HttpListener();
        _dinleyici.Prefixes.Add($"http://{ipAdresi}:{port}/");
        _dinleyici.Start();
        Console.WriteLine($"HTTP sunucusu {ipAdresi}:{port} adresinde başlatıldı...");

        try
        {
            while (true)
            {
                HttpListenerContext context = _dinleyici.GetContext();
                ThreadPool.QueueUserWorkItem(Istekİşle, context);
            }
        }
        finally
        {
            _dinleyici.Stop();
            Console.WriteLine("HTTP sunucusu durduruldu.");
        }
    }

    static void Istekİşle(object state)
    {
        HttpListenerContext context = (HttpListenerContext)state;
        DateTime istekZamani = DateTime.Now;
        string istemciIp = context.Request.RemoteEndPoint.ToString().Split(':')[0];

        var bağlantıVerisi = _ipBağlantıları.GetOrAdd(istemciIp, (0, istekZamani, 0, DateTime.MinValue));

        lock (_kilit)
        {
            if (bağlantıVerisi.banSonu > DateTime.Now)
            {
                BanSayfasınıGönder(context);
                return;
            }

            if ((istekZamani - bağlantıVerisi.sonİstekZamani) > _bağlantıPenceresi)
            {
                _ipBağlantıları[istemciIp] = (1, istekZamani, bağlantıVerisi.banSayisi, DateTime.MinValue);
            }
            else
            {
                if (bağlantıVerisi.sayac >= _maksBağlantıSayısı)
                {
                    bağlantıVerisi.banSayisi++;
                    if (bağlantıVerisi.banSayisi >= 3)
                    {
                        _ipBağlantıları[istemciIp] = (0, DateTime.Now, 0, DateTime.MaxValue);
                    }
                    else
                    {
                        int banSüresi = 60 * (int)Math.Pow(2, bağlantıVerisi.banSayisi - 1);
                        _ipBağlantıları[istemciIp] = (0, DateTime.Now, bağlantıVerisi.banSayisi, DateTime.Now.AddSeconds(banSüresi));
                    }
                    BanSayfasınıGönder(context);
                    return;
                }
                else
                {
                    _ipBağlantıları[istemciIp] = (bağlantıVerisi.sayac + 1, bağlantıVerisi.sonİstekZamani, bağlantıVerisi.banSayisi, bağlantıVerisi.banSonu);
                }
            }
        }

        context.Response.KeepAlive = false;

        try
        {
            string dosyaYolu = Path.Combine("sablon", "index.html");
            if (File.Exists(dosyaYolu))
            {
                context.Response.ContentType = "text/html";
                context.Response.ContentLength64 = new FileInfo(dosyaYolu).Length;
                context.Response.StatusCode = (int)HttpStatusCode.OK;

                using (FileStream fs = File.OpenRead(dosyaYolu))
                {
                    fs.CopyTo(context.Response.OutputStream);
                }
            }
            else
            {
                context.Response.StatusCode = (int)HttpStatusCode.NotFound;
            }
        }
        catch (IOException ex)
        {
            Console.WriteLine($"Bağlantı hatası: {ex.Message}");
        }
        finally
        {
            context.Response.Close();
        }
    }

    static void BanSayfasınıGönder(HttpListenerContext context)
    {
        string banSayfası = Path.Combine("sablon", "ban.html");
        context.Response.StatusCode = (int)HttpStatusCode.Forbidden;
        context.Response.ContentType = "text/html";

        try
        {
            if (File.Exists(banSayfası))
            {
                using (FileStream fs = File.OpenRead(banSayfası))
                {
                    fs.CopyTo(context.Response.OutputStream);
                }
            }
            else
            {
                context.Response.StatusCode = (int)HttpStatusCode.NotFound;
            }
        }
        catch (IOException ex)
        {

        }
        finally
        {
            context.Response.Close();
        }
    }
}
