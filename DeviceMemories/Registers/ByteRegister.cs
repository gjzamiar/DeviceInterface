﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ECore.DeviceMemories
{
    public class ByteRegister : MemoryRegister
    {
        private byte internalValue;

        public ByteRegister(int address, string name) : base(address, name) { }

        public override MemoryRegister Set(object value)
        {
            byte castValue;
            try
            {
                castValue = (byte)value;
                if (!value.Equals(castValue))
                    throw new Exception("Cast to byte resulted in loss of information");
            }
            catch (InvalidCastException)
            {
                throw new Exception("Cannot set ByteRegister with that kind of type (" + value.GetType().Name + ")");
            }
            this.internalValue = (byte)value;
            CallValueChangedCallbacks();
            return this;
        }
        
        public override object Get() { return this.internalValue; }
        public byte GetByte() { return this.internalValue; }
        public override int MaxValue { get { return 255; } }
    }
}
