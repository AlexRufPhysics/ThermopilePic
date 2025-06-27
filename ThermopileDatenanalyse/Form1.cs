using System;
using System.IO.Ports;
using System.Windows.Forms;
using OxyPlot;
using OxyPlot.Series;
using OxyPlot.WindowsForms;
using OxyPlot.Axes;
using System.Timers;
using System.Drawing.Text;
using System.Text.Json.Serialization.Metadata;



namespace ThermopileDatenanalyse
{
    public partial class Form1 : Form
    {
        private string port = "COM1";
        private System.Windows.Forms.Timer updateTimer = new System.Windows.Forms.Timer();
        private int baudRate = 115200;

        const int RowPerBlock = 7;
        const int PixelPerRow = 120;
        const int NumberOfBlocks = 6;
        const int DataPos = 2;
        const int PixelPerColumn = RowPerBlock * 2 * NumberOfBlocks;
        const int DataBlockLengthPerBlock = 2 * PixelPerRow * RowPerBlock + 10;
        const int TotalBlocks = 2 * NumberOfBlocks + 3;
        const int BackgroundStackSize = 10;
       


        private SerialPort serialPort = null!;
        private PlotModel heatmapModel = null!;
        private HeatMapSeries heatmapSeries = null!;

        private byte[][] RAMoutput = new byte[TotalBlocks][];
        private double[,] pixelData = new double[PixelPerColumn, PixelPerRow];
        private double[,] background = new double[PixelPerColumn, PixelPerRow];


        private Boolean printToBackground = false;
        private double[,,] BackgroundStack = new double[BackgroundStackSize, PixelPerColumn, PixelPerRow];
        private int backStackCount = 0;


        public Form1()
        {
            InitializeComponent();
            InitSerialPort();
            InitPlot();
            InitTimer();
        }

        private void plotView2_Click(object sender, EventArgs e)
        {

        }

        private void button1_Click(object sender, EventArgs e)
        {
            try
            {
                if (!serialPort.IsOpen)
                    serialPort.Open();

                updateTimer.Start(); 
            }
            catch (Exception ex)
            {
                MessageBox.Show("Fehler beim Starten: " + ex.Message);
            }

        }


        private void button2_Click(object sender, EventArgs e)
        {
            setBackground();
        }


        private void InitSerialPort()
        {
            serialPort = new SerialPort(port, baudRate);
            serialPort.ReadTimeout = 1000;
            serialPort.WriteTimeout = 500;
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
                Data = new double[PixelPerColumn ,  PixelPerRow]
            };

            heatmapModel.Series.Add(heatmapSeries);
            heatmapModel.Axes.Add(new LinearColorAxis { Position = AxisPosition.Right });

            plotView2.Model = heatmapModel;
        }

        private void InitTimer()
        {
            updateTimer.Interval = 100; // 100ms → ca. 10Hz
            updateTimer.Tick += UpdateTimer_Tick;
        }

        private void UpdateTimer_Tick(object? sender, EventArgs e)
        {
            getData();
            if (printToBackground == true)
            {
                fillBackground();  
            }

        }

        private void getData()
        {
            try
            {
                serialPort.Write("b");

                // Alle Datenblöcke lesen
                for (int i = 0; i < TotalBlocks; i++)
                {
                    RAMoutput[i] = new byte[DataBlockLengthPerBlock];
                    int bytesRead = 0;
                    while (bytesRead < DataBlockLengthPerBlock)
                    {
                        bytesRead += serialPort.Read(RAMoutput[i], bytesRead, DataBlockLengthPerBlock - bytesRead);
                    }
                }

                ConvertToPixelData();
                UpdateHeatmap();
            }
            catch (Exception ex)
            {
                updateTimer.Stop();
                MessageBox.Show("Fehler beim Lesen/Zeichnen: " + ex.Message);
            }

            
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
                for(int j = 0;j < PixelPerRow; j++)
                {
                    for(int k = 0; k < BackgroundStackSize; k++)
                    {
                        background[i, j] += BackgroundStack[k, i, j];
                    }
                    background[i, j] /= BackgroundStackSize; 
                }
            }

        }
        

        private void ConvertToPixelData()
        {
            ushort pos;
            for (int m = 0; m < RowPerBlock; m++)
            {
                for (int n = 0; n < PixelPerRow; n++)
                {
                    pos = (ushort)(2 * n + DataPos + m * 2 * PixelPerRow);

                    for (int i = 0; i < NumberOfBlocks; i++)
                    {
                        // top half
                        pixelData[m + i * RowPerBlock, n] =
                            (ushort)(RAMoutput[i][pos] << 8 | RAMoutput[i][pos + 1]);
                        

                        // bottom half		

                        pixelData[PixelPerColumn - 1 - m - i * RowPerBlock, n] =
                            (ushort)(RAMoutput[2 * NumberOfBlocks + 2 - i - 1][pos] << 8 | RAMoutput[2 * NumberOfBlocks + 2 - i - 1][pos + 1]);
                    }
                }
            }

        }

        private void UpdateHeatmap()
        {
            
            heatmapSeries.Data = pixelData;

            plotView2.InvalidatePlot(true);

  
        }

        
    }


}
