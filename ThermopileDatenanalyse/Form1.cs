using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using OxyPlot.WindowsForms;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows.Forms;



namespace ThermopileDatenanalyse
{
    public partial class Form1 : Form
    {
        const int UDP_PACKET_LENGTH = 1401;
        const int LAST_UDP_PACKET_LENGTH = 1149;
        const int NUMBER_OF_PACKETS_PER_FRAME = 17; 
        const int Port = 30444;
        const string IP = "192.168.4.1";


        const int RowPerBlock = 7;
        const int PixelPerRow = 120;
        const int NumberOfBlocks = 6;
        const int DataPos = 2;
        const int PixelPerColumn = 84;


        const int DataBlockLengthPerBlock = 2 * PixelPerRow * RowPerBlock + 10;
        const int TotalBlocks = 2 * NumberOfBlocks + 3;
        const int BackgroundStackSize = 10;

        private UdpClient udpClient = null!;
        private IPEndPoint remoteEP = new IPEndPoint(IPAddress.Any, 0);
        private IPEndPoint espEndpoint = new IPEndPoint(IPAddress.Parse(IP), Port);

        private byte[][] receivedPackets = new byte[NUMBER_OF_PACKETS_PER_FRAME][];
        private bool[] packetReceivedFlags = new bool[NUMBER_OF_PACKETS_PER_FRAME];
        private int receivedPacketCount = 0;




        private PlotModel heatmapModel = null!;
        private HeatMapSeries heatmapSeries = null!;

        private double[,] pixelData = new double[PixelPerColumn, PixelPerRow];
        private double[,] background = new double[PixelPerColumn, PixelPerRow];
        private double[,] picture = new double[PixelPerColumn, PixelPerRow];



        List <double[,]> BackgroundStack = new List<double[,]>();
        const int BackgroundFrameStack = 50;
        




        public Form1()
        {
            InitializeComponent();
            InitUdp();
            InitPlot();
        }

        private void plotView2_Click(object sender, EventArgs e)
        {

        }

        private void button1_Click(object sender, EventArgs e)
        {


            string bindMessage = "Bind HTPA series device";
            byte[] data = Encoding.ASCII.GetBytes(bindMessage);
            udpClient.Send(data, data.Length, espEndpoint);
        }


        private void button2_Click(object sender, EventArgs e)
        {
            setBackground();
        }

        private void button3_Click(object sender, EventArgs e)
        {
            string startMessage = "t";
            byte[] data = Encoding.ASCII.GetBytes(startMessage);
            udpClient.Send(data, data.Length, espEndpoint);
        }

        private void button4_Click(object sender, EventArgs e)
        {
            string startMessage = "x";
            byte[] data = Encoding.ASCII.GetBytes(startMessage);
            udpClient.Send(data, data.Length, espEndpoint);

        }


        private void InitUdp()
        {
            udpClient = new UdpClient(Port);
            udpClient.BeginReceive(UdpReceiveCallback, null);
        }





        private void UdpReceiveCallback(IAsyncResult ar)
        {
            try
            {
                byte[] receivedBytes = udpClient.EndReceive(ar, ref remoteEP);

                // Paket-Index bestimmen (angenommen erstes Byte = Paketnummer 0..6)
                int packetIndex = receivedBytes[0]-1; // Beispiel, bitte anpassen je nach Protokoll

                if (packetIndex >= 0 && packetIndex < NUMBER_OF_PACKETS_PER_FRAME)
                {
                    // Paket speichern
                    receivedPackets[packetIndex] = receivedBytes;
                    packetReceivedFlags[packetIndex] = true;
                }

                // Prüfen, ob alle Pakete da sind
                if (AllPacketsReceived())
                {
                    byte[] fullFrame = CombinePackets();
                    ProcessFullFrame(fullFrame);

                    // Reset für nächsten Frame
                    ResetPackets();
                }
            }
            catch (Exception ex)
            {
                // Fehlerbehandlung
                //Console.WriteLine("UDP Receive Fehler: " + ex.Message);
                Console.WriteLine("UDP Receive Fehler: " + ex.StackTrace);
            }
            finally
            {
                udpClient.BeginReceive(UdpReceiveCallback, null);
            }
        }

        private bool AllPacketsReceived()
        {
            foreach (var flag in packetReceivedFlags)
                if (!flag) return false;
            return true;
        }

        private byte[] CombinePackets()
        {
            int fullLength = UDP_PACKET_LENGTH * (NUMBER_OF_PACKETS_PER_FRAME - 1) + LAST_UDP_PACKET_LENGTH;
            byte[] fullFrame = new byte[fullLength];

            for (int i = 0; i < NUMBER_OF_PACKETS_PER_FRAME - 1; i++)
            {
                Array.Copy(receivedPackets[i], 1, fullFrame, i * (UDP_PACKET_LENGTH-1) , UDP_PACKET_LENGTH -1);
            }

            // Letztes Paket, ggf. kleiner
            Array.Copy(receivedPackets[NUMBER_OF_PACKETS_PER_FRAME - 1], 1,
                       fullFrame, (NUMBER_OF_PACKETS_PER_FRAME - 1) * (UDP_PACKET_LENGTH-1),
                       LAST_UDP_PACKET_LENGTH-1);

            return fullFrame;
        }

        private void ProcessFullFrame(byte[] fullFrame)
        {
            int fullFrameIndex = 0;
            for (int m = 0; m < PixelPerColumn; m++)
            {
                for (int n = 0; n < PixelPerRow; n++)
                {
                    pixelData[m, n] =
                            (ushort)(fullFrame[fullFrameIndex]  | fullFrame[fullFrameIndex +1] << 8);   

                    fullFrameIndex += 2;
                }
            }

            if (BackgroundStack.Count >= BackgroundFrameStack)
            {
                BackgroundStack.RemoveAt(0);
            }
            BackgroundStack.Add(pixelData);

            buildPicture();
            
        }

        private void buildPicture()
        {
            for (int i = 0; i < PixelPerColumn; i++)
            {
                for (int j = 0; j < PixelPerRow; j++)
                {
                    picture[i,j] = pixelData[i,j] - background[i,j];
                }
            }

            this.Invoke((MethodInvoker)delegate
            {
                heatmapSeries.Data = picture;
                heatmapModel.InvalidatePlot(true);
            });
        }

        private void ResetPackets()
        {
            for (int i = 0; i < NUMBER_OF_PACKETS_PER_FRAME; i++)
            {
                packetReceivedFlags[i] = false;
                receivedPackets[i] = null!;
            }
            receivedPacketCount = 0;
        }




        private void InitPlot()
        {
            heatmapModel = new PlotModel { Title = "Live Heatmap" };

            heatmapSeries = new HeatMapSeries
            {
                X0 = 0,
                X1 = PixelPerRow,
                Y0 = 0,
                Y1 = PixelPerColumn,
                Interpolate = false,
                RenderMethod = HeatMapRenderMethod.Bitmap,
                Data = new double[PixelPerColumn, PixelPerRow]
            };

            heatmapModel.Series.Add(heatmapSeries);
            heatmapModel.Axes.Add(new LinearColorAxis { Position = AxisPosition.Right });

            plotView2.Model = heatmapModel;
        }

        private void setBackground()
        {
            for (int i = 0; i < PixelPerColumn; i++)
            {
                for (int j = 0; j < PixelPerRow; j++)
                {
                    double sum = 0.0;
                    for (int k = 0; k < BackgroundFrameStack; k++)
                    {
                        sum += BackgroundStack[k][i, j];
                    }
                    background[i,j] = sum/BackgroundFrameStack;
                }
            }
        }


        






        

        
    }


}
