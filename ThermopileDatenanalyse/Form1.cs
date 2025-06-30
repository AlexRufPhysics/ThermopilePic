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
        const int PixelPerColumn = RowPerBlock * 2 * NumberOfBlocks;


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


        private Boolean printToBackground = false;
        private double[,,] BackgroundStack = new double[BackgroundStackSize, PixelPerColumn, PixelPerRow];
        private int backStackCount = 0;




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
                Console.WriteLine("UDP Receive Fehler: " + ex.Message);
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
                Array.Copy(receivedPackets[i], 0, fullFrame, i * UDP_PACKET_LENGTH, UDP_PACKET_LENGTH);
            }

            // Letztes Paket, ggf. kleiner
            Array.Copy(receivedPackets[NUMBER_OF_PACKETS_PER_FRAME - 1], 0,
                       fullFrame, (NUMBER_OF_PACKETS_PER_FRAME - 1) * UDP_PACKET_LENGTH,
                       LAST_UDP_PACKET_LENGTH);

            return fullFrame;
        }

        private void ProcessFullFrame(byte[] fullFrame)
        {

            int DataBlockLengthPerBlock = 2 * PixelPerRow * RowPerBlock + 10;
            int TotalBlocks = 2 * NumberOfBlocks + 3; // wie vorher

            byte[][] RAMoutput = new byte[TotalBlocks][];
            for (int i = 0; i < TotalBlocks; i++)
            {
                RAMoutput[i] = new byte[DataBlockLengthPerBlock];
                Array.Copy(fullFrame, i * DataBlockLengthPerBlock, RAMoutput[i], 0, DataBlockLengthPerBlock);
            }

            // pixelData füllen
            ushort pos;
            for (int m = 0; m < RowPerBlock; m++)
            {
                for (int n = 0; n < PixelPerRow; n++)
                {
                    pos = (ushort)(2 * n + DataPos + m * 2 * PixelPerRow);

                    for (int i = 0; i < NumberOfBlocks; i++)
                    {
                        // obere Hälfte
                        pixelData[m + i * RowPerBlock, n] =
                            (ushort)(RAMoutput[i][pos] << 8 | RAMoutput[i][pos + 1]);

                        // untere Hälfte
                        pixelData[PixelPerColumn - 1 - m - i * RowPerBlock, n] =
                            (ushort)(RAMoutput[2 * NumberOfBlocks + 2 - i - 1][pos] << 8 | RAMoutput[2 * NumberOfBlocks + 2 - i - 1][pos + 1]);
                    }
                }
            }

            // Plot im UI aktualisieren, im UI-Thread
            this.Invoke((MethodInvoker)delegate
            {
                heatmapSeries.Data = pixelData;
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
            backStackCount = 0;
            printToBackground = true;

        }


        private void fillBackground()
        {

            for (int j = 0; j < PixelPerColumn; j++)
            {
                for (int k = 0; k < PixelPerRow; k++)
                {
                    BackgroundStack[backStackCount, j, k] = pixelData[j, k];
                }
            }
            backStackCount++;
            if (backStackCount > 9)
            {
                printToBackground = false;
                newBackground();
            }

        }


        private void newBackground()
        {
            for (int i = 0; i < PixelPerColumn; i++)
            {
                for (int j = 0; j < PixelPerRow; j++)
                {
                    for (int k = 0; k < BackgroundStackSize; k++)
                    {
                        background[i, j] += BackgroundStack[k, i, j];
                    }
                    background[i, j] /= BackgroundStackSize;
                }
            }

        }




        

        
    }


}
