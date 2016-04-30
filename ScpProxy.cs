﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using System.Text;

using System.Net;
using System.Net.Sockets;

namespace ScpControl 
{
    public partial class ScpProxy : Component 
    {
        protected static Char[] m_Delim = new Char[] { '^' };

        protected IPEndPoint m_ServerEp = new IPEndPoint(IPAddress.Loopback, 26760);
        protected UdpClient  m_Server   = new UdpClient();

        protected IPEndPoint m_ClientEp = new IPEndPoint(IPAddress.Loopback, 26761);
        protected UdpClient  m_Client   = new UdpClient();

        protected XmlMapper   m_Mapper   = new XmlMapper();
        protected Boolean     m_Active   = false;

        public event EventHandler<DsPacket> Packet = null;

        public virtual XmlMapper Mapper 
        {
            get { return m_Mapper; }
        }

        public virtual String Active 
        {
            get 
            {
                String Active = String.Empty;
                Byte[] Send = { 0, 6 };

                if (m_Server.Send(Send, Send.Length, m_ServerEp) == Send.Length)
                {
                    IPEndPoint ReferenceEp = new IPEndPoint(IPAddress.Loopback, 0);

                    Byte[] Buffer = m_Server.Receive(ref ReferenceEp);

                    if (Buffer.Length > 0)
                    {
                        String Data = Encoding.Unicode.GetString(Buffer);
                        String[] Split = Data.Split(m_Delim, StringSplitOptions.RemoveEmptyEntries);

                        Active = Split[0];
                    }
                }

                return Active;
            }
        }


        public ScpProxy() 
        {
            InitializeComponent();
        }

        public ScpProxy(IContainer container) 
        {
            container.Add(this);

            InitializeComponent();
        }


        public virtual Boolean Start() 
        {
            try
            {
                if (!m_Active)
                {
                    NativeFeed_Worker.RunWorkerAsync();
                    m_Active = true;
                }
            }
            catch { }

            return m_Active;
        }

        public virtual Boolean Stop() 
        {
            try
            {
                if (m_Active)
                {
                    NativeFeed_Worker.CancelAsync();
                    m_Active = false;
                }
            }
            catch { }

            return !m_Active;
        }


        public virtual Boolean Load() 
        {
            Boolean Loaded = false;

            try
            {
                Byte[] Buffer = { 0, 0x08 };

                if (m_Server.Send(Buffer, Buffer.Length, m_ServerEp) == Buffer.Length)
                {
                    IPEndPoint ReferenceEp = new IPEndPoint(IPAddress.Loopback, 0);

                    Buffer = m_Server.Receive(ref ReferenceEp);

                    if (Buffer.Length > 0)
                    {
                        String Data = Encoding.UTF8.GetString(Buffer);

                        //m_Mapper.Initialize();
                    }
                }

                Loaded = true;
            }
            catch { }

            return Loaded;
        }

        public virtual Boolean Select(Profile Target) 
        {
            Boolean Selected = false;

            try
            {
                if (m_Active)
                {
                    Byte[] Data = Encoding.Unicode.GetBytes(Target.Name);
                    Byte[] Send = new Byte[Data.Length + 2];

                    Send[1] = 0x07;
                    Array.Copy(Data, 0, Send, 2, Data.Length);

                    m_Server.Send(Send, Send.Length, m_ServerEp);

                    SetDefault(Target);
                    Selected = true;
                }
            }
            catch { }

            return Selected;
        }

        public virtual DsDetail Detail(DsPadId Pad) 
        {
            DsDetail Detail = null;

            try
            {
                Byte[] Buffer = { (Byte) Pad, 0x0A };

                if (m_Server.Send(Buffer, Buffer.Length, m_ServerEp) == Buffer.Length)
                {
                    IPEndPoint ReferenceEp = new IPEndPoint(IPAddress.Loopback, 0);

                    Buffer = m_Server.Receive(ref ReferenceEp);

                    if (Buffer.Length > 0)
                    {
                        Byte[] Local = new Byte[6]; Array.Copy(Buffer, 5, Local, 0, Local.Length);

                        Detail = new DsDetail((DsPadId) Buffer[0], (DsState) Buffer[1], (DsModel) Buffer[2], Local, (DsConnection) Buffer[3], (DsBattery) Buffer[4]);
                    }
                }
            }
            catch { }

            return Detail;
        }


        public virtual Boolean Rumble(Int32 Pad, Byte Large, Byte Small) 
        {
            Boolean Rumbled = false;

            try
            {
                if (m_Active)
                {
                    Byte[] Buffer = { (Byte) Pad, 0x01, Large, Small };

                    m_Server.Send(Buffer, Buffer.Length, m_ServerEp);
                    Rumbled = true;
                }
            }
            catch { }

            return Rumbled;
        }

        public virtual Boolean Remap(String Target, DsPacket Packet) 
        {
            Boolean Remapped = false;

            try
            {
                if (m_Active)
                {
                    Byte[] Output = new Byte[Packet.Native.Length];

                    switch (Packet.Detail.Model)
                    {
                        case DsModel.DS3: if (m_Mapper.RemapDs3(m_Mapper.Map[Target], Packet.Native, Output)) { Array.Copy(Output, Packet.Native, Output.Length); Packet.Remapped(); } break;
                    }

                    Remapped = true;
                }
            }
            catch { }

            return Remapped;
        }


        public virtual Boolean SetDefault(Profile Profile) 
        {
            Boolean Set = true;

            try
            {
                foreach (Profile Item in m_Mapper.Map.Values)
                {
                    Item.Default = false;
                }

                Profile.Default = true;
            }
            catch { Set = false; }

            return Set;
        }

        protected virtual void NativeFeed_Worker_DoWork(object sender, DoWorkEventArgs e) 
        {
            IPEndPoint Remote = new IPEndPoint(IPAddress.Loopback, 0);

            m_Client = new UdpClient(m_ClientEp);
            m_Client.Client.ReceiveTimeout = 500;

            while(!NativeFeed_Worker.CancellationPending)
            {
                try
                {
                    Byte[] Buffer = m_Client.Receive(ref Remote);

                    LogPacket(new DsPacket(Buffer));
                }
                catch { }
            }

            m_Client.Close();
            e.Cancel = true;
        }

        protected virtual void LogPacket(DsPacket Data) 
        {
            if (Packet != null)
            {
                Packet(this, Data);
            }
        }
    }

    public class DsPacket : EventArgs 
    {
        protected Int32         m_Packet;
        protected DsDetail      m_Detail;
        protected Byte[]        m_Native;

        protected Ds3Button     m_Ds3Button = Ds3Button.None;

        internal DsPacket(Byte[] Native) 
        {
            Byte[] Local = new Byte[6];

            Array.Copy(Native, (Int32) DsOffset.Address, Local, 0, Local.Length);

            m_Detail = new DsDetail(
                    (DsPadId)      Native[(Int32) DsOffset.Pad       ],
                    (DsState)      Native[(Int32) DsOffset.State     ],
                    (DsModel)      Native[(Int32) DsOffset.Model     ],
                    Local,
                    (DsConnection) Native[(Int32) DsOffset.Connection],
                    (DsBattery)    Native[(Int32) DsOffset.Battery   ]
                    );

            m_Packet = (Int32)(Native[4] << 0 | Native[5] << 8 | Native[6] << 16 | Native[7] << 24);
            m_Native = Native;

            switch(m_Detail.Model)
            {
                case DsModel.DS3: m_Ds3Button = (Ds3Button)((Native[10] << 0) | (Native[11] << 8) |  (Native[12] << 16) | (Native[13] << 24)); break;
            }
        }


        internal Byte[] Native   
        {
            get { return m_Native; }
        }

        internal void Remapped() 
        {
            switch (m_Detail.Model)
            {
                case DsModel.DS3: m_Ds3Button = (Ds3Button)((Native[10] << 0) | (Native[11] << 8) |  (Native[12] << 16) | (Native[13] << 24)); break;
            }
        }


        public DsDetail Detail 
        {
            get { return m_Detail; }
        }


        public Boolean Button(Ds3Button Flag) 
        {
            if (m_Detail.Model != DsModel.DS3) throw new InvalidEnumArgumentException();

            return m_Ds3Button.HasFlag(Flag);
        }

        public Byte Axis(Ds3Axis Offset) 
        {
            if (m_Detail.Model != DsModel.DS3) throw new InvalidEnumArgumentException();

            return Native[(Int32) Offset];
        }

    }

    public class DsDetail 
    {
        protected DsPadId      m_Serial;
        protected DsModel      m_Model;
        protected Byte[]       m_Local = new Byte[6];
        protected DsConnection m_Mode;
        protected DsBattery    m_Charge;
        protected DsState      m_State;

        internal DsDetail(DsPadId PadId, DsState State, DsModel Model, Byte[] Mac, DsConnection Mode, DsBattery Level) 
        {
            m_Serial = PadId;
            m_State  = State;
            m_Model  = Model;
            m_Mode   = Mode;
            m_Charge = Level;

            Array.Copy(Mac, m_Local, m_Local.Length);
        }


        public DsPadId Pad       
        {
            get { return m_Serial; }
        }

        public DsState State     
        {
            get { return m_State; }
        }

        public DsModel Model     
        {
            get { return m_Model; }
        }

        public String Local      
        {
            get { return String.Format("{0:X2}:{1:X2}:{2:X2}:{3:X2}:{4:X2}:{5:X2}", m_Local[0], m_Local[1], m_Local[2], m_Local[3], m_Local[4], m_Local[5]); }
        }

        public DsConnection Mode 
        {
            get { return m_Mode; }
        }

        public DsBattery Charge  
        {
            get { return m_Charge; }
        }
    }
}