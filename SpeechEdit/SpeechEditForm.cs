using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Collections;
using System.Windows.Forms.DataVisualization.Charting;
using System.IO;
using SpeechEdit;
using SignalProcessing;

namespace SpeechEdit
{
    public partial class SpeechEditForm : Form
    {
        #region 全局变量
        // 数据读取
        #region WAV文件结构内容描述
        public int SamplePerSec;                                 // 采样率
        public string Channels;                                     // 通道数信息
        public string BitsPerSample;
        public int Data_Size;
        public int Data_Len;
        public double Wav_Time;                                  // wav数据时间长度
        #endregion
        public ArrayList openedWavPath = new ArrayList();            // 存放已打开的(.wav)文件完整路径
        public double[][] speech_read = new double[500][];           // 已经读取的语音序列
        public ArrayList speech_index = new ArrayList();                 // 已经读取的语音序列的原始索引
        public int speech_num = 0;                                                  // 打开过的语音序列个数

        // 播放
        public System.Media.SoundPlayer player;                           // 音频播放类实例化


        // 短时分析
        public int frame_len = 512;                    // 统一帧长
        public int frame_overlap = 256;             // 统一重叠
        public Matrix speech_divided = new Matrix();    // 分帧后形成的数据矩阵
        public Complex[][] spectrum_divided;                // 当前分析数据的每一帧的频谱
        public Matrix Cn;                                                 // 当前分析数据的每一帧的倒谱
        public int frame_selected = 0;                            // 选择帧数
        
        // 绘图
        public int mode = 0;                              // 绘图模式

        // 数据信息
        public double speech_minimum = 0;    // 语音最大幅值
        public double speech_maximum = 0;   // 语音最小幅值
        #endregion

        #region 方法函数
        public double[] ReadWavData(string filepath) // 读取Wav文件数据
        {
            #region 初始化
            double[] speech_sequence = new double[Data_Len];     // 存放wav数据
            #endregion
            #region WAV文件结构
            // RIFF WAVE Chunck
            byte[] riff_id = new byte[4];
            byte[] riff_size = new byte[4];
            byte[] riff_type = new byte[4];

            // Format Chunck
            byte[] format_id = new byte[4];
            byte[] format_size = new byte[4];
            byte[] format_tag = new byte[2];
            byte[] channels = new byte[2];                 // 通道数
            byte[] samplepersec = new byte[4];         // 采样率
            byte[] avgbytespersec = new byte[4];      // 每秒字节数
            byte[] blockalign = new byte[2];
            byte[] bitspersample = new byte[2];
            byte[] additionalinfo = new byte[2];         // 附加信息 若Size = 18，则多出该附加信息 Size =16 则无

            // Fact Chunk    (可选结构) 
            byte[] fact_id = new byte[4];
            byte[] fact_size = new byte[4];
            byte[] fact_data = new byte[4];

            // Data Chunk
            byte[] data_id = new byte[4];
            byte[] data_size = new byte[4];

            #endregion
            // 读取wav文件的每项内容
            using (FileStream fs = new FileStream(@filepath, FileMode.Open, FileAccess.Read))
            {
                using (BinaryReader br = new BinaryReader(fs, Encoding.UTF8))
                {
                    // RIFF WAVE Chunck
                    br.Read(riff_id, 0, 4);                                // 'RIFF'
                    br.Read(riff_size, 0, 4);
                    br.Read(riff_type, 0, 4);                            // 'WAVE'

                    // Format Chunck
                    br.Read(format_id, 0, 4);                         // 'fmt'
                    br.Read(format_size, 0, 4);                      // "0018H" or "006H"
                    br.Read(format_tag, 0, 2);
                    br.Read(channels, 0, 2);
                    br.Read(samplepersec, 0, 4);
                    br.Read(avgbytespersec, 0, 4);
                    br.Read(blockalign, 0, 2);
                    br.Read(bitspersample, 0, 2);
                    if (format_size[0].ToString() == "18")
                    {
                        br.Read(additionalinfo, 0, 2);
                    }
                    SamplePerSec = ByteArray2Int(samplepersec);
                    Channels = channels[0].ToString();
                    BitsPerSample = bitspersample[0].ToString();

                    // Fact Chunk
                    byte[] id = new byte[4];
                    br.Read(id, 0, 4);
                    if (GetString(id, 4) == "fact")
                    {
                        fact_id = id;
                        br.Read(fact_size, 0, 4);
                        br.Read(fact_data, 0, 4);
                        br.Read(data_id, 0, 4);
                        br.Read(data_size, 0, 4);
                    }
                    // Data Chunk
                    else
                    {
                        data_id = id;
                        br.Read(data_size, 0, 4);
                    }
                    Data_Size = ByteArray2Int(data_size);

                    if (BitsPerSample == "8")
                    {
                        speech_sequence = new double[Data_Size];
                        for (int i = 0; i < Data_Size; i++)
                        {
                            byte wavdt = br.ReadByte();
                            speech_sequence[i] = (double)wavdt / 32767;
                        }
                        Wav_Time = (double)Data_Size / SamplePerSec;
                        Data_Len = Data_Size;
                    }
                    else if (BitsPerSample == "16")
                    {
                        speech_sequence = new double[Data_Size / 2];
                        for (int i = 0; i < Data_Size / 2; i++)
                        {
                            short wavdt = br.ReadInt16();
                            speech_sequence[i] = (double)wavdt / 32767;
                        }
                        Wav_Time = (double)Data_Size / 2 / SamplePerSec;
                        Data_Len = Data_Size / 2;
                    }
                    br.Close();
                }
                fs.Close();
            }
            return speech_sequence;
        }
        private string GetString(byte[] bts, int len)      // byte to string
        {
            char[] tmp = new char[len];
            for (int i = 0; i < len; i++)
            {
                tmp[i] = (char)bts[i];
            }
            return new string(tmp);
        }
        private int ByteArray2Int(byte[] byteArray)      // byte[] to int
        {
            return byteArray[0] | (byteArray[1] << 8) | (byteArray[2] << 16) | (byteArray[3] << 24);
        }
        public void Plot(double[] yValues, int mode)   // 绘图
        {
            ClearPlot(mode);
            Series series;
            ChartArea chartArea;
            // 在chart中显示数据
            int yValues_len = yValues.Length;
            switch(mode)
            {
                case 0:            // 只绘制波形图
                    series = chart1.Series[0];
                    for (int i = 0; i < yValues_len; i++)
                    {
                        series.Points.AddXY((double)i / SamplePerSec, yValues[i]);
                    }
                    //设置显示范围
                    ToolShow(false);
                    chartArea = chart1.ChartAreas[0];
                    chartArea.AxisX.Minimum = 0;
                    chartArea.AxisX.Maximum = Math.Floor((double)yValues_len / SamplePerSec * 1000) / 1000;
                    chartArea.AxisY.Minimum = -1.1;
                    chartArea.AxisY.Maximum = 1.1;
                    chartArea.AxisY.Interval = 2.2/4;
                    break;
                case 1:           // 只改变chart2
                    ToolShow(true);
                    series = chart2.Series[0];
                    for (int i = 0; i < yValues_len; i++)
                    {
                        series.Points.AddXY(i, yValues[i]);
                    }
                    chartArea = chart2.ChartAreas[0];
                    chartArea.AxisX.Minimum = 1;
                    chartArea.AxisX.Maximum = yValues_len - 1;
                    chartArea.AxisY.Minimum = -1.1;
                    chartArea.AxisY.Maximum = 1.1;
                    chartArea.AxisY.Interval = 2.2 / 4;
                    break;
                case 2:           // 只改变chart3
                    ToolShow(true);
                    series = chart3.Series[0];
                    for (int i = 0; i < yValues_len; i++)
                    {
                        series.Points.AddXY(i, yValues[i]);
                    }
                    chartArea = chart3.ChartAreas[0];
                    chartArea.AxisX.Minimum = 0;
                    chartArea.AxisX.Maximum = (yValues_len - 1);
                    break;
            }
        }
        public void ShowInfo(int mode)
        {
            switch (mode)
            {
                case 0:            // 显示chart1信息
                    WavInfo_rtbox.Text = "采样率:" + SamplePerSec.ToString() + "\n" + "通道数量:" + Channels + "\n" + "数据大小:" + 
                        string.Format("{0:N3}", Wav_Time) + "秒" + "\n" + "最小值:" + string.Format("{0:0.000}", speech_minimum) + "\n"
                        + "最大值:" + string.Format("{0:0.000}", speech_maximum);
                    break;
                case 2:            // 显示chart3信息
                    break;
            }
        }
        public void ClearPlot(int mode)  // 清除绘图
        {
            switch (mode)
            {
                case 0:
                    chart1.Series[0].Points.Clear();
                    chart2.Series[0].Points.Clear();
                    chart3.Series[0].Points.Clear();
                    break;
                case 1:
                    chart2.Series[0].Points.Clear();
                    break;
                case 2:
                    chart3.Series[0].Points.Clear();
                    break;
            }
        }
        public void ToolShow(bool show) // 部分控件设置
        {
            label1.Visible = show;
            label2.Visible = show;
            FrameSelect_nud.Enabled = show;
            FrameSelect_nud.Visible = show;
            Windows_cbox.Enabled = show;
            Windows_cbox.Visible = show;
        }
        #endregion

        public SpeechEditForm()
        {
            InitializeComponent();
        }

        #region 控件事件
        private void SpeechEdit_Load(object sender, EventArgs e)
        {
            // 部分控件不可见
            label1.Visible = false;
            label2.Visible = false;
            FrameSelect_nud.Visible = false;
            Windows_cbox.Visible = false;

            FrameSelect_nud.Minimum = 0;       // 定义能够选择的最小帧数为 0
            Windows_cbox.DropDownStyle = ComboBoxStyle.DropDownList;  // 下拉列表样式

            //添加窗类型
            Windows_cbox.Items.Add("矩形窗");
            Windows_cbox.Items.Add("三角窗");
            Windows_cbox.Items.Add("汉宁窗");
            Windows_cbox.Items.Add("汉明窗");
            Windows_cbox.Items.Add("布莱克曼窗");
        }
        private void OpenedWav_lb_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (OpenedWav_lb.SelectedIndex != -1)          // 选中一个目标
            {
                int index = (int)speech_index[OpenedWav_lb.SelectedIndex];
                Plot(speech_read[index], 0);                                  // 画出选中文件的波形
            }
        }
        private void FrameSelect_nud_ValueChanged(object sender, EventArgs e)
        {
            frame_selected = (int)FrameSelect_nud.Value;         // 选择帧
            switch(mode)
            {
                case 1:
                    Plot(speech_divided.Values[frame_selected], 1);      // 绘制该帧的波形
                    break;
                case 2:
                    Plot(speech_divided.Values[frame_selected], 1);      // 绘制该帧的波形
                    Plot(Complex.Abs(spectrum_divided[frame_selected]), 2);  // 绘制该帧的频谱波形
                    break;
                case 3:
                    Plot(speech_divided.Values[frame_selected], 1);      // 绘制该帧的波形
                    Plot(Cn.Values[frame_selected], 2);
                    break;
            }
        }
        #endregion

        #region 工具栏事件
        private void OpenWav_tsb_Click(object sender, EventArgs e)
        {
            打开ToolStripMenuItem_Click(null, null);
        }
        private void PlayAudio_tsb_Click(object sender, EventArgs e)
        {
            播放ToolStripMenuItem_Click(null, null);
        }
        private void PlayAudio_tsb_DoubleClick(object sender, EventArgs e)
        {
            循环播放ToolStripMenuItem_Click(null, null);
        }
        private void StopPlay_tsb_Click(object sender, EventArgs e)
        {
            停止播放ToolStripMenuItem_Click(null, null);
        }
        private void STFT_tsb_Click(object sender, EventArgs e)
        {
            短时傅里叶分析ToolStripMenuItem_Click(null, null);
        }
        private void STEnergy_tsb_Click(object sender, EventArgs e)
        {
            能量曲线ToolStripMenuItem_Click(null, null);
        }
        private void ZCR_tsb_Click(object sender, EventArgs e)
        {
            过零率ToolStripMenuItem_Click(null, null);
        }
        #endregion

        #region 菜单栏事件
        // 文件
        private void 打开ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            /*
             * openWavFileDialog.Filter = "WAV Files|*.wav*"   
             * 只显示(.wav)格式的文件
             */
            if (openWavFileDialog.ShowDialog() == DialogResult.OK)
            {
                string filedir = openWavFileDialog.FileName;  // 获取打开文件的完整路径
                FileInfo finfo = new FileInfo(filedir);                  // 实例化FileInfo对象
                string wavpath = finfo.Name;                           // 获取所打开 .wav 文件的文件名 
                OpenedWav_lb.Items.Add(wavpath);                       // 向 listbox 控件中添加该文件
                openedWavPath.Add(filedir);                            // 动态保存 .wav 文件的文件路径

                speech_index.Add(speech_num);                                  // 添加原始索引
                speech_read[speech_num] = ReadWavData(filedir);    // 动态保存已读语音数据

                // 画wav波形图
                Plot(speech_read[speech_num], 0);

                // 显示波形信息
                speech_minimum = speech_read[speech_num].Min();
                speech_maximum = speech_read[speech_num].Max();
                ShowInfo(0);
                speech_num += 1;                                                        // 语音个数+1
            }
        }

        private void 退出ToolStripMenuItem1_Click(object sender, EventArgs e)
        {
            this.Dispose();
        }
        // 声音
        private void 播放ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (OpenedWav_lb.SelectedItems.Count == 0)
            {
                MessageBox.Show("请选择一个需要播放的音频！");                                   // 则提示未选中任何文件
            }
            else
            {
                string wavpath = (string)openedWavPath[OpenedWav_lb.SelectedIndex];           // 获取选中文件的完整路径
                System.Media.SoundPlayer player = new System.Media.SoundPlayer(wavpath);
                player.Play();     // 单次播放选中音频
            }
        }
        private void 循环播放ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (OpenedWav_lb.SelectedItems.Count == 0)
            {
                MessageBox.Show("请选择一个需要播放的音频！");
            }
            else
            {
                string wavpath = (string)openedWavPath[OpenedWav_lb.SelectedIndex];            // 获取选中文件的完整路径
                player = new System.Media.SoundPlayer(wavpath);
                player.PlayLooping();         // 循环播放选中音频
            }
        }
        private void 停止播放ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            player.Stop();
        }
        // 分析
        private void 短时傅里叶分析ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (OpenedWav_lb.SelectedItems.Count == 0)
            {
                MessageBox.Show("请选择一个需要分析的音频！");                                   // 则提示未选中任何文件
            }
            else
            {
                mode = 2;

                // 选择已经读取过序列
                int index = (int)speech_index[OpenedWav_lb.SelectedIndex];         
                double[] speech_sequence = speech_read[index];                  

                speech_divided = new Vector(speech_sequence).DivFrame(this.frame_len, this.frame_overlap);    // 语音分帧
                FrameSelect_nud.Maximum = speech_divided.Rows-1;       // 设置帧数选择框最大值

                // 求每一帧的频谱
                spectrum_divided = speech_divided.FFT(this.frame_len);
                Plot(speech_divided.Values[0], 1);
                Plot(Complex.Abs(spectrum_divided[0]), 2);
            }
        }
        private void 能量曲线ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (OpenedWav_lb.SelectedItems.Count == 0)
            {
                MessageBox.Show("请选择一个需要分析的音频！");                                   // 则提示未选中任何文件
            }
            else
            {
                mode = 1;

                // 选择已经读取过序列
                int index = (int)speech_index[OpenedWav_lb.SelectedIndex];
                double[] speech_sequence = speech_read[index];

                speech_divided = new Vector(speech_sequence).DivFrame(this.frame_len, this.frame_overlap);    // 语音分帧
                FrameSelect_nud.Maximum = speech_divided.Rows - 1;       // 设置帧数选择框最大值

                // 求每一帧的频谱
                spectrum_divided = speech_divided.FFT(this.frame_len);

                // 绘图
                Plot(speech_divided.Values[0], 1);
                Plot(speech_divided.STEnergy(), 2);
            }
        }
        private void 过零率ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (OpenedWav_lb.SelectedItems.Count == 0)
            {
                MessageBox.Show("请选择一个需要分析的音频！");                                   // 则提示未选中任何文件
            }
            else
            {
                mode = 1;

                // 选择已经读取过序列
                int index = (int)speech_index[OpenedWav_lb.SelectedIndex];
                double[] speech_sequence = speech_read[index];

                speech_divided = new Vector(speech_sequence).DivFrame(this.frame_len, this.frame_overlap);    // 语音分帧
                FrameSelect_nud.Maximum = speech_divided.Rows - 1;       // 设置帧数选择框最大值

                // 求每一帧的频谱
                spectrum_divided = speech_divided.FFT(this.frame_len);

                // 绘图
                Plot(speech_divided.Values[0], 1);
                Plot(speech_divided.ZCR(), 2);
            }
        }
        private void 帧倒谱ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (OpenedWav_lb.SelectedItems.Count == 0)
            {
                MessageBox.Show("请选择一个需要分析的音频！");                                   // 则提示未选中任何文件
            }
            else
            {
                mode = 3;

                // 选择已经读取过序列
                int index = (int)speech_index[OpenedWav_lb.SelectedIndex];
                double[] speech_sequence = speech_read[index];

                speech_divided = new Vector(speech_sequence).DivFrame(this.frame_len, this.frame_overlap);    // 语音分帧
                FrameSelect_nud.Maximum = speech_divided.Rows - 1;       // 设置帧数选择框最大值

                // 求每一帧的频谱和倒谱
                spectrum_divided = speech_divided.FFT(this.frame_len);
                Cn = Complex.Cepstrum(spectrum_divided);

                //Rn = speech_divided.AutoCorrelation();  // 自相关分析

                // 绘图
                Plot(speech_divided.Values[0], 1);
                Plot(Cn.Values[0], 2);
            }
        }
        #endregion
    }
}

namespace SignalProcessing
{
    public class Complex   // 定义复数类
    {
        #region 属性
        private double real;        // 实部
        private double imag;      // 虚部
        public double Real { get { return real; } set { real = value; } }
        public double Imag { get { return imag; } set { imag = value; } }
        public double Mod { get { return Math.Sqrt(real * real + imag * imag); } }   // 模长
        public double Norm{ get { return imag * imag + real * real;  } }                   // 范数
        #endregion
        #region 构造函数
        public Complex() { }
        public Complex(double real, double imag) // 创建单个复数对象
        {
            this.real = real;
            this.imag = imag;
        }
        #endregion
        #region 运算符
        public static Complex operator +(Complex A, Complex B)
        {
            return new Complex(A.real + B.real, A.imag + B.imag);
        }
        public static Complex operator -(Complex A, Complex B)
        {
            return new Complex(A.real - B.real, A.imag - B.imag);
        }
        public static Complex operator *(Complex A, Complex B)
        {
            return new Complex(A.real * B.real - A.imag * B.imag, A.real * B.imag + A.imag * B.real);
        }
        public static Complex operator /(Complex A, Complex B)
        {
            if (A.real == 0 && B.imag == 0)
            {
                throw new Exception("除数不能为零！");
            }
            return new Complex((A * Conjugate(B)).real / B.Norm, (A * Conjugate(B)).imag / B.Norm);
        }
        #endregion
        #region 方法函数
        public static Complex Conjugate(Complex c)    // 共轭
        {
            return new Complex(c.real, -c.imag);
        }
        public static Complex[] Conjugate(Complex[]c_sequence) // 对复数序列取共轭
        {
            Complex[] c_conj = new Complex[c_sequence.Length];
            for (int i = 0; i < c_sequence.Length; i++)
            {
                c_conj[i] = Conjugate(c_sequence[i]);
            }
            return c_conj;
        }
        public static Complex[][] Conjugate(Complex[][] c_matrix) // 对复数矩阵取共轭
        {
            Complex[][] c_conj = new Complex[c_matrix.GetLength(0)][];
            for (int i = 0; i < c_matrix.GetLength(0); i++)
            {
                c_conj[i] = Conjugate(c_matrix[i]);
            }
            return c_conj;
        }
        public static double[] Abs(Complex[] c_sequence)  // 取复数序列每个元素的模
        {
            double[] mod = new double[c_sequence.Length];
            for (int i = 0; i < c_sequence.Length; i++)
            {
                mod[i] = c_sequence[i].Mod;
            }
            return mod;
        }
        public static Matrix Abs(Complex[][] c_matrix)
        {
            double[][] mod = new double[c_matrix.GetLength(0)][];
            for (int i = 0; i < c_matrix.GetLength(0); i++)
            {
                mod[i] = Complex.Abs(c_matrix[i]);
            }
            return new Matrix(mod);
        }
        public static double[] Reals(Complex[] c_sequence) // 取复数序列的实部
        {
            double[] reals = new double[c_sequence.Length];
            for(int i =0; i<c_sequence.Length; i++)
            {
                reals[i] = c_sequence[i].real;
            }
            return reals;
        }
        public static Matrix Reals(Complex[][] c_matrix)       // 取复数矩阵的实部
        {
            double[][] reals = new double[c_matrix.GetLength(0)][];
            for (int i = 0; i < c_matrix.GetLength(0); i++)
            {
                reals[i] = Complex.Reals(c_matrix[i]);
            }
            return new Matrix(reals);
        }
        public static double[] Imags(Complex[] c_sequence) // 取复数序列的虚部
        {
            double[] imags = new double[c_sequence.Length];
            for (int i = 0; i < c_sequence.Length; i++)
            {
                imags[i] = c_sequence[i].imag;
            }
            return imags;
        }
        public static Matrix Cepstrum(Complex[][] c_matrix)  // 倒谱分析
        {
            return Complex.Reals((Complex.Abs(c_matrix).Log(Math.E)).IFFT());
        }
        public override string ToString()                                 // 重写ToString()
        {
            if (this.real != 0 && this.imag != 0)
            {
                if (this.imag > 0)
                {
                    return string.Format("<{0}+j{1}>", this.real, this.imag);
                }
                else
                {
                    return string.Format("<{0}-j{1}>", this.real, -this.imag);
                }
            }
            else if (this.real == 0 && this.imag != 0)
            {
                if (this.imag > 0)
                {
                    return string.Format("<j{0}>", this.imag);
                }
                else
                {
                    return string.Format("<-j{1}>", this.real, -this.imag);
                }
            }
            else
            {
                return string.Format("<{0}>", this.real);
            }
        }
        #endregion
    }
    public class Vector : Operation // 序列类
    {
        #region 属性
        private double[] values;      // 序列值
        public double[] Values { get { return values; } set { values = value; } }
        public int Length { get { return this.values.Length; } }  // 序列长度
        public double Sum { get { return this.values.Sum(); } } // 序列的和
        public double Min { get { return this.values.Min(); } } // 序列的最小值
        public double Max { get { return this.values.Max(); } } // 序列的最大值
        #endregion
        #region 构造函数
        public Vector(double[] vector)//构造序列对象
        {
            this.values = vector;
        }
        public Vector() { }
        #endregion
        #region 运算符
        public static Vector operator +(Vector A, Vector B) // 序列相加
        {
            if (A.Length != B.Length)
            {
                throw new Exception("输入参数维度不匹配");
            }
            else
            {
                double[] results = new double[A.Length];
                for (int i = 0; i < A.Length; i++)
                {
                    results[i] = A.values[i] + B.values[i];
                }
                return new Vector(results);
            }
        }
        public static Vector operator -(Vector A, Vector B) // 序列相减
        {
            if (A.Length != B.Length)
            {
                throw new Exception("输入参数维度不匹配");
            }
            else
            {
                double[] results = new double[A.Length];
                for (int i = 0; i < A.Length; i++)
                {
                    results[i] = A.values[i] - B.values[i];
                }
                return new Vector(results);
            }
        }
        public static Vector operator *(Vector A, Vector B) // 序列相乘
        {
            if (A.Length != B.Length)
            {
                throw new Exception("输入参数维度不匹配");
            }
            else
            {
                double[] results = new double[A.Length];
                for (int i = 0; i < A.Length; i++)
                {
                    results[i] = A.values[i] * B.values[i];
                }
                return new Vector(results);
            }
        }
        #endregion
        #region 方法函数
        public override string ToString()    // 重写ToString
        {
            string vec_str = "";
            foreach(double num in this.values)
            {
                vec_str += "<" + string.Format("{0:N2}", num) + "> ";
            }
            return vec_str;
        }
        public Vector GetValues(int start, int stop)  // 序列索引
        {
            if(start > stop)
            {
                throw new Exception("索引错误!");
            }
            else
            {
                int len = stop - start + 1;
                double[] v_slice = new double[len];
                for(int i = 0; i< len; i++)
                {
                    v_slice[i] = this.values[i + start];
                }
                return new Vector(v_slice);
            }
        }
        public Vector ZeroPad(int len_padded, out int bits_num)   // 序列后补零
        {
            if (this.Length > len_padded)
            {
                throw new Exception("补零长度小于序列长度！");
            }
            bits_num = (int)Math.Ceiling(Math.Log(len_padded) / Math.Log(2));   // 计算补零后所需要的比特数
            double[] x = new double[len_padded];
            for(int i = 0; i<this.Length; i++)
            {
                x[i] = this.values[i];
            }

            return new Vector(x);
        }
        public Complex[] FFT(int N_fft)     // FFT运算
        {
            /*
             *  <summary>
             *  功能: 快速傅里叶变换
             *  double[] x: 输入序列
             *  N_fft : FFT点数
             */

            double[] x_padded = this.ZeroPad(N_fft, out int bits_num).values;      // 后补零序列
            int[] inv_order = FFT_Order(N_fft, bits_num);                                    // 倒序下标
            Complex[] spectrum = new Complex[N_fft];                                     // 创建复数序列
            Complex[] butterfly_result = new Complex[2];                                 // 存放蝶形运算的中间结果
            int k, step, offset;

            // 将实数序列变为复数表示，并进行第一轮蝶形运算
            for (int i = 0; i < N_fft / 2; i++)                                                          
            {
                butterfly_result = Butterfly(new Complex(x_padded[inv_order[2 * i]], 0), new Complex(x_padded[inv_order[2 * i + 1]], 0), N_fft, 0);
                spectrum[2 * i] = butterfly_result[0];
                spectrum[2 * i + 1] = butterfly_result[1];
            }

            // 进行第fft_loop轮蝶形运算
            for (int fft_loop = bits_num - 2; fft_loop > 0; fft_loop--)
            {
                k = (int)Math.Pow(2, fft_loop);
                step = N_fft / 2 / k;
                offset = 2 * step;
                for (int i = 0; i < k; i++)
                {
                    for (int j = 0; j < step; j++)
                    {
                        butterfly_result = Butterfly(spectrum[i * offset + j], spectrum[i * offset + j + step], N_fft, j * k);
                        spectrum[i * offset + j] = butterfly_result[0];
                        spectrum[i * offset + j + step] = butterfly_result[1];
                    }
                }
            }

            // 进行最后一轮蝶形运算
            step = N_fft / 2;
            for (int j = 0; j < step; j++)
            {
                butterfly_result = Butterfly(spectrum[j], spectrum[j + step], N_fft, j);
                spectrum[j] = butterfly_result[0];
                spectrum[j + step] = butterfly_result[1];
            }
            return spectrum;
        }
        public Matrix DivFrame(int frame_len, int frame_overlap)   // 语音分帧
        {
            /*                                                    
            *  <summary>                                              
            *  功能：语音分帧                                             
            *  speech_sequence : 原始语音序列
            *  frame_num : 帧数
            */

            int step = frame_len - frame_overlap;
            int frame_num = (this.Length - frame_overlap) / step;               // 分出的帧数
            double[][] speech_divided = new double[frame_num][];             // 存储分帧后的数据矩阵

            for (int i = 0; i < frame_num; i++)                                                // 为每一行分配内存
            {
                speech_divided[i] = new double[frame_len];
            }

            for (int i = 0; i < frame_num - 1; i++)
            {
                for (int j = 0; j < frame_len; j++)
                {
                    speech_divided[i][j] = this.values[i * step + j];
                }
            }
            return new Matrix(speech_divided);
        }
        public Vector Log(double e)         // 对每个元素取对数
        {
            double[] v_log = new double[this.Length];
            if(Math.E ==e)
            {
                for (int i = 0; i < this.Length; i++)
                {
                    v_log[i] = Math.Log(this.values[i]);
                }
            }
            else if(e==10)
            {
                for (int i = 0; i < this.Length; i++)
                {
                    v_log[i] = Math.Log10(this.values[i]);
                }
            }
            return new Vector(v_log);
        }
        public Vector AutoCorrelation()   // 序列自相关分析
        {
            double[] Rn = new double[this.Length];
            for(int i = 0; i<this.Length; i++)
            {
                Rn[i] = (this.GetValues(0, this.Length-1 - i) * this.GetValues(i, this.Length-1)).Sum;
            }
            return new Vector(Rn);
        }
        #endregion
    }
    public class Matrix : Operation
    {
        #region 属性
        private double[][] values;      // 序列值
        public double[][] Values { get { return values; } set { values = value; } }
        public int Rows { get { return this.values.GetLength(0); } }  // 矩阵行数
        public int Cols { get { return this.values[0].Length; } }     //矩阵列数
        public double[] Min // 序列的最小值
        {
            get
            {
                double[] min = new double[this.Rows];
                for (int i = 0; i < this.Rows; i++)
                {
                    min[i] = this.values[i].Min();
                }
                return min;
            }
        }
        public double[] Max // 序列的最大值
        {
            get
            {
                double[] max = new double[this.Rows];
                for (int i = 0; i < this.Rows; i++)
                {
                    max[i] = this.values[i].Max();
                }
                return max;
            }
        }
        #endregion
        #region 构造函数
        public Matrix() { }
        public Matrix(double[][] mat) // 构造矩阵对象
        {
            this.values = mat;
        }
        #endregion
        #region 运算符
        public static Matrix operator +(Matrix A, Matrix B) // 矩阵相加
        {
            if (A.Rows != B.Rows || A.Cols != B.Cols)
            {
                throw new Exception("输入参数维度不匹配");
            }
            else
            {
                double[][] results = new double[A.Rows][];
                for (int i = 0; i < A.Rows; i++)
                {
                    for (int j = 0; j < A.Cols; j++)
                    {
                        results[i][j] = A.values[i][j] + B.values[i][j];
                    }
                }
                return new Matrix(results);
            }
        }
        public static Matrix operator -(Matrix A, Matrix B) // 矩阵相减
        {
            if (A.Rows != B.Rows || A.Cols != B.Cols)
            {
                throw new Exception("输入参数维度不匹配");
            }
            else
            {
                double[][] results = new double[A.Rows][];
                for (int i = 0; i < A.Rows; i++)
                {
                    for (int j = 0; j < A.Cols; j++)
                    {
                        results[i][j] = A.values[i][j] - B.values[i][j];
                    }
                }
                return new Matrix(results);
            }
        }
        public static Matrix operator *(double A, Matrix B) // 矩阵标量乘
        {
            for (int i = 0; i < B.Rows; i++)
            {
                for (int j = 0; j < B.Cols; j++)
                {
                    B.values[i][j] *= A;
                }
            }
            return B;
        }
        public static Matrix operator *(Matrix A, Matrix B) // 矩阵乘
        {
            if (A.Cols != B.Rows)
            {
                throw new Exception("输入参数维度不匹配");
            }
            else
            {
                double[][] results = new double[A.Rows][];
                for (int i = 0; i < A.Rows; i++)
                {
                    results[i] = new double[B.Cols];
                    for (int j = 0; j < B.Cols; j++)
                    {
                        for (int k = 0; k < B.Rows; k++)
                        {
                            results[i][j] += A.values[i][k] * B.values[k][j];
                        }
                    }
                }
                return new Matrix(results);
            }
        }
        #endregion
        #region 方法函数
        public override string ToString()    // 重写ToString
        {
            string mat_str = "";
            for(int i = 0; i<this.Rows; i++)
            {
                for(int j =0; j<this.Cols; j++)
                {
                    if(j == 0)
                    {
                        mat_str += "[" + string.Format("{0:N2}", this.values[i][j]) + " ";
                    }
                    else if(j==this.Cols-1)
                    {
                        mat_str += string.Format("{0:N2}", this.values[i][j]) + "]\n";
                    }
                    else
                    {
                        mat_str += string.Format("{0:N2}", this.values[i][j]) + " ";
                    }
                }
            }
            return mat_str;
        }
        public Matrix Tranpose()  // 矩阵转置
        {
            double[][] mat_tranposed = new double[this.Cols][];
            for(int i = 0; i< this.Cols; i++)
            {
                mat_tranposed[i] = new double[this.Rows];
                for (int j = 0; j<this.Rows; j++)
                {
                    mat_tranposed[i][j] = this.values[j][i];
                }
            }
            return new Matrix(mat_tranposed);
        }
        public Matrix Log(double e)   // 对矩阵每个元素取底为e的对数
        {
            double[][] mat_log = new double[this.Rows][];
            for(int i = 0; i<this.Rows; i++)
            {
                mat_log[i] = (new Vector(this.values[i]).Log(e)).Values;
            }
            return new Matrix(mat_log);
        }
        public Matrix ZeroPad(int len_padded, out int bits_num) //矩阵每行后补零
        {
            if (this.Cols > len_padded)
            {
                throw new Exception("补零长度小于矩阵列数！");
            }

            bits_num = (int)Math.Ceiling(Math.Log(len_padded) / Math.Log(2));
            double[][] x = new double[this.Rows][];
            for(int i=0; i<this.Rows; i++)
            {
                x[i] = new double[len_padded];
                for (int j = 0; j<this.Cols; j++)
                {
                    x[i][j] = this.values[i][j];
                }
            }
            return new Matrix(x);
        }
        public Complex[][] FFT(int N_fft)  // 对矩阵每行进行FFT
        {
            double[][] x_padded = this.ZeroPad(N_fft, out int bits_num).values;     // 后补零序列
            int[] inv_order = FFT_Order(N_fft, bits_num);                                    // 倒序下标
            Complex[][] spectrum = new Complex[this.Rows][];                             // 创建复数序列
            Complex[] butterfly_result = new Complex[2];                                 // 存放蝶形运算的中间结果
            int k, step, offset;

            // 将实数序列变为复数表示，并进行第一轮蝶形运算
            for(int r = 0; r<this.Rows; r++)
            {
                spectrum[r] = new Complex[N_fft];
                for (int i = 0; i < N_fft / 2; i++)
                {
                    butterfly_result = Butterfly(new Complex(x_padded[r][inv_order[2 * i]], 0), new Complex(x_padded[r][inv_order[2 * i + 1]], 0), N_fft, 0);
                    spectrum[r][2 * i] = butterfly_result[0];
                    spectrum[r][2 * i + 1] = butterfly_result[1];
                }

                // 进行第fft_loop轮蝶形运算
                for (int fft_loop = bits_num - 2; fft_loop > 0; fft_loop--)
                {
                    k = (int)Math.Pow(2, fft_loop);
                    step = N_fft / 2 / k;
                    offset = 2 * step;
                    for (int i = 0; i < k; i++)
                    {
                        for (int j = 0; j < step; j++)
                        {
                            butterfly_result = Butterfly(spectrum[r][i * offset + j], spectrum[r][i * offset + j + step], N_fft, j * k);
                            spectrum[r][i * offset + j] = butterfly_result[0];
                            spectrum[r][i * offset + j + step] = butterfly_result[1];
                        }
                    }
                }

                // 进行最后一轮蝶形运算
                step = N_fft / 2;
                for (int j = 0; j < step; j++)
                {
                    butterfly_result = Butterfly(spectrum[r][j], spectrum[r][j + step], N_fft, j);
                    spectrum[r][j] = butterfly_result[0];
                    spectrum[r][j + step] = butterfly_result[1];
                }
            }
            return spectrum;
        }
        public Complex[][] IFFT()             // 对矩阵每行进行IFFT
        {
            return  Complex.Conjugate((((double)1 / this.Cols) * this).FFT(this.Cols));
        }
        public double[] STEnergy()   // 短时能量分析
        {
            double[] En = new double[this.Rows];
            Matrix mat = this * this.Tranpose();
            for (int i = 0; i < this.Rows; i++)
            {
                    En[i] += mat.values[i][i];
            }
            return En;
        }
        public double[] ZCR()         // 短时过零率分析
        {
            double[] Zn = new double[this.Rows];
            for (int i = 0; i < this.Rows; i++)
            {
                for (int j = 1; j < this.Cols; j++)
                {
                    Zn[i] += Math.Abs(Math.Sign(this.values[i][j]) - Math.Sign(this.values[i][j - 1])) / 2;
                }
            }
            return Zn;
        }
        public Matrix AutoCorrelation()  // 矩阵每行数据进行自相关分析
        {
            double[][] Rn = new double[this.Rows][];
            for(int i = 0; i<this.Rows; i++)
            {
                Rn[i] = (new Vector(this.Values[i]).AutoCorrelation()).Values;
            }
            return new Matrix(Rn);
        }
        #endregion
    }
    public class Operation     // 公用的特殊运算
    {
        public Complex[] Butterfly(Complex X, Complex Y, int N_fft, int k) // 蝶形运算
        {
            /*                                                    
             *  <summary>                                              
             *  功能：做蝶形运算                                               
             *  N_fft : 做FFT的点数                                             
             *  k : 第 k 轮蝶形运算                                             
             */
            Complex Wnk = new Complex(Math.Cos(2 * Math.PI * k / (double)N_fft), -Math.Sin(2 * Math.PI * k / (double)N_fft));     // 旋转因子
            Complex[] Output = new Complex[2];       // 蝶形运算的输出                                                                                               
            Output[0] = X + Y * Wnk;
            Output[1] = X - Y * Wnk;
            return Output;
        }
        public int[] FFT_Order(int N_fft, int bits_num)      // 输入倒序
        {
            /*
             * <summary>  
             * 功能：根据输入数据的的序号的二进制倒转来重新排序
             * 例如：
             * 输入x(0) x(1) x(2) x(3) x(4) x(5) x(6) x(7)
             * 二进制序号000 001 010 011 100 101 110 111
             * 二进制反序000 100 010 110 001 101 011 111
             * 输出x(0) x(4) x(2) x(6) x(1) x(5) x(3) x(7)
             * 返回值y为行向量
            */

            int[] inv_index = new int[N_fft];
            char[] bisequence;
            for (int i = 0; i < N_fft; i++)
            {
                bisequence = Convert.ToString(i, 2).PadLeft(bits_num, '0').ToArray();
                Array.Reverse(bisequence, 0, bits_num);
                inv_index[i] = Convert.ToInt32(new string(bisequence), 2);
            }
            return inv_index;
        }
    }
}
