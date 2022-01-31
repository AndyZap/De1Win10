#!/usr/bin/env python
'''
Created on Mar 15, 2017

@author: Ray
'''
from __future__ import print_function
import Scan, struct, sys, time, traceback
from collections import namedtuple
from EntryParser import EntryParser
from bluepy.btle import Peripheral, ADDR_TYPE_RANDOM, BTLEException, DefaultDelegate
from args import first
from array import array
from time import sleep

# Unfortunately, Bluepy forces an Object Oriented solution to handling notifications
class MyDelegate(DefaultDelegate):
  def __init__(self):
    DefaultDelegate.__init__(self)
    self.Handlers = {}

  def handleNotification(self, cHandle, data):
    callback, params = self.Handlers[cHandle]
    callback(params, data)
    
  def addHandler(self, ctic, fn, params):
    """
    Add a handler for the given characteristic. Fn will be called with
    params and the arriving data when a notification occurs
    """
    print("Registered %s for handle %d" % (fn, ctic.getHandle()))
    self.Handlers[ctic.getHandle()] = (fn, params)
        
NotifyDelegate = MyDelegate()
T_FWMapRequest = namedtuple('T_FWMapRequest', 'WindowIncrement, FWToErase, FWToMap, FirstError')

"""
/**
 * In order to bypass all the BLE restrictions, I'll mostly be adding new things using the
 * Memory Mapped Region interface.
 *
 * The idea is that everything is mapped into a memory map. Clients will read the region
 * as necessary and work from that.
 *
 * To read a MMR write the address you want to read to the Address field, then wait for a
 * notify with the data you asked for, tagged by address.
 */
struct PACKEDATTR T_ReadFromMMR {
  U8P0  Len;       // Length of data to read, in words-1. ie. 0 = 4 bytes, 1 = 8 bytes, 255 = 2014 bytes, etc.
  U24P0 Address;   // Address of window. Will autoincrement if set up in MapRequest
  U8P0  Data[16];  // If data reaches past the end of a region, bytes will be zero filled
};

struct PACKEDATTR T_WriteToMMR {
  U8P0  Len;       // Length of data
  U24P0 Address;   // Address within the MMR
  U8P0  Data[16];  // Data, zero padded
};
"""

T_ReadFromMMR = namedtuple("T_ReadFromMMR", 'Len, Address, Data')
T_WriteToMMR = namedtuple("T_WriteToMMR", 'Len, Address, Data')

def toU24P0(val):
  hi  = (val >> 16) & 0xFF
  mid = (val >> 8 ) & 0xFF
  lo  = (val      ) & 0xFF
  return struct.pack(">BBB", hi, mid, lo)

def toU8P4int(val):
  return int(round(val*16))

def toU8P4(val):
  return struct.pack(">B", toU8P4int(val))

def toU8P1int(val):
  return int(round(val*2))

def toU8P1(val):
  return struct.pack(">B", toU8P1(val))

def toU8P0int(val):
  return int(round(val))

def toU8P0(val):
  return struct.pack(">B", toU8P0int(val))

def toU10P0int(val):
  return int(round(val)) & 0x3FF

def fromU24P0(val):
  (hi, mid, lo) = struct.unpack(">BBB", val)
  return (hi << 16) + (mid << 8) + lo;

def fromU32P0LE(val):
  return struct.unpack("<I", val)[0]

def fromU8P0(val):
  print(repr(val), val)
  (lo,) = struct.unpack(">B", val)
  return lo;
 
def toF8_1_7int(x):
  if x >= 12.75:
    # High bit is the exponent. 0 = 10^-1, 1 = 10^0
    return int(round(x)) | 0x80
  else:
    return int(round(x*10.0))
  

def read_FWMapRequest(ctic):
  # Read the characteristic and parse as a FWMapRequest
  blebuffer = ctic.read()
  (windowinc, fw2erase, fw2map, ferr) = struct.unpack('>HBB3s', blebuffer)
  return T_FWMapRequest(windowinc, fw2erase, fw2map, fromU24P0(ferr))

"""
struct PACKEDATTR T_ShotDescHeader {
  U8P0 HeaderV;           // Set to 1 for this type of shot description
  U8P0 NumberOfFrames;    // Total number of unextended frames.
  U8P0 NumberOfPreinfuseFrames; // Number of frames that are preinfusion
  U8P4 MinimumPressure;   // In flow priority modes, this is the minimum pressure we'll allow
  U8P4 MaximumFlow;       // In pressure priority modes, this is the maximum flow rate we'll allow
};
"""
def write_ShotDescHeader(ctic, NumFrames, NumPIFrames, withResponse=True):
  data = struct.pack('>BBBBB', 1, toU8P0int(NumFrames), toU8P0int(NumPIFrames), 0, 0)
  ctic.write(data, withResponse=withResponse)
  
"""
struct PACKEDATTR T_ShotFrame {
  U8P0   Flag;       // See T_E_FrameFlags
  U8P4   SetVal;     // SetVal is a 4.4 fixed point number, setting either pressure or flow rate, as per mode
  U8P1   Temp;       // Temperature in 0.5 C steps from 0 - 127.5
  F8_1_7 FrameLen;   // FrameLen is the length of this frame in seconds. It's a 1/7 bit floating point number as described in the F8_1_7 a struct
  U8P4   TriggerVal; // Trigger value. Could be a flow or pressure.
  U10P0  MaxVol;     // Exit current frame if the total shot volume/weight exceeds this value. 0 means ignore
};
"""
def write_ShotFrameToFrameWrite(ctic, FrameNum, Flags, SetVal, Temp, FrameLen, TriggerVal, MaxVol, withResponse=True):
  data = struct.pack('>BBBBBBH',
    toU8P0int(FrameNum), 
    toU8P0int(Flags), 
    toU8P4int(SetVal), 
    toU8P1int(Temp), 
    toF8_1_7int(FrameLen), 
    toU8P4int(TriggerVal), 
    toU10P0int(MaxVol))
  ctic.write(data, withResponse=withResponse)

"""
struct PACKEDATTR T_ShotExtFrame {
  // Extension frame. The data in this section is added to existing frames. Extension frame 32 extends frame 0, 33 extends 1, etc.
  U8P4 MaxFlowOrPressure;  // In flow profiles, this is where the pressure OPV kicks in. In Pressure, flow starts being limited at this value.
  U8P4 MaxFoPRange;        // This is the approximate maximum range of the flow or pressure. I suggest 10% of MaxFlowOrPressure for pressure, and 20% for flow.
                           // Another way to view it: MaxFlowOrPressure is where the OPV kicks in. MaxFlowOrPressure+MaxFoPRange is the point that it is impossible to
                           // exceed. The OPV will probably manage to retard the setpoint so that the output is retarded before this.
                           // Another way to put it. A large range gives a soft and slow response to the OPV. A short range a very hard one. Too short will act weird,
                           // as the setpoint will be retarded down to zero almost instantly, making things stop and start. Don't do it.
  U8 Unused[5];
};
"""

def write_ShotExtFrameToFrameWrite(ctic, FrameNum, MaxFlowOrPressure, MaxFoPRange, withResponse=True):
  data = struct.pack('>BBBBBBBB', toU8P0int(FrameNum), toU8P4int(MaxFlowOrPressure), toU8P4int(MaxFoPRange), 0, 0, 0, 0, 0)
  ctic.write(data, withResponse=withResponse)


"""
// Tail extension frame. One of these goes after the sequence of frames, and has extra global info about t
  // The total allowed volume of the shot
  // High bit of the U16 is 1 if we want to ignore preinfusion volume after preinfusion is done.
  // This is to prevent preinfusion from exceeding the max volume.
  U10P0 MaxTotalVolume;
  U8 Unused[5];
"""

def write_ShotTailToFrameWrite(ctic, FrameNum, MaxTotalVolume, UsePI, withResponse=True):
  orval = 0
  if UsePI:
    orval = 0x400

  data = struct.pack('>BHBBBBB', toU8P0int(FrameNum), toU10P0int(MaxTotalVolume)|orval, 0, 0, 0, 0, 0)
  ctic.write(data, withResponse=withResponse)

def write_FWMapRequest(ctic, WindowIncrement=0, FWToErase=0, FWToMap=0, FirstError=0, withResponse=True):
  data = struct.pack('>HBB3s', WindowIncrement, FWToErase, FWToMap, toU24P0(FirstError))
  ctic.write(data, withResponse=withResponse)
  
def write_FWWriteToMMR(ctic, data, address, withResponse=True):
  ld = len(data)
  ps = '>B3s%ds%ds' % (ld, 16-ld)
  
  blob = struct.pack(ps, ld, toU24P0(address), data, b'\0'*(16-ld) )
  ctic.write(blob, withResponse=withResponse)

def write_FWReadFromMMR(ctic, dlen, address, withResponse=True):
  blob = struct.pack('>B3s16s', dlen, toU24P0(address), b'\x00'*16)
  ctic.write(blob, withResponse=withResponse)
  
def registerForNotifyCallback(ctic, fn, params):
  NotifyDelegate.addHandler(ctic, fn, params)
  ctic.peripheral.writeCharacteristic(ctic.getHandle()+1, struct.pack("<H", 1))  
  
def hexDump(data):
  return ":".join("{:02x}".format(ord(c)) for c in data)

"""
static T_Versions        I_Versions        = VERSIONINFO; // A001 A R    Versions See T_Versions
static T_RequestedState  I_RequestedState ; // A002 B RW   RequestedState See T_RequestedState
static T_SetTime         I_SetTime        ; // A003 C RW   SetTime Set current time
static T_ShotDirectory   I_ShotDirectory  ; // A004 D R    ShotDirectory View shot directory
static T_ReadFromMMR     I_ReadFromMMR    ; // A005 E RW   ReadFromMMR Read bytes from data mapped into the memory mapped region.
static T_WriteToMMR      I_WriteToMMR     ; // A006 F W    WriteToMMR Write bytes to memory mapped region
static T_ShotMapRequest  I_ShotMapRequest ; // A007 G W    ShotMapRequest Map a shot so that it may be read/written
static T_DeleteShotRange I_DeleteShotRange; // A008 H W    DeleteShotRange Delete all shots in the range given
static T_FWMapRequest    I_FWMapRequest   ; // A009 I W    FWMapRequest Map a firmware image into MMR. Cannot be done with the boot image
static T_Temperatures    I_Temperatures   ; // A00A J R    Temperatures See T_Temperatures
static T_ShotSettings    I_ShotSettings   ; // A00B K RW   ShotSettings See T_ShotSettings
static T_Deprecated      I_Deprecated     ; // A00C L RW   Deprecated Was T_ShotDesc. Now deprecated.
static T_ShotSample      I_ShotSample     ; // A00D M R    ShotSample Use to monitor a running shot. See T_ShotSample
static T_StateInfo       I_StateInfo       = {T_Enum_API_MachineStates::Init, T_Enum_API_Substates::NoState}; // A00E N R    StateInfo The current state of the DE1
static T_HeaderWrite     I_HeaderWrite    ; // A00F O RW   HeaderWrite Use this to change a header in the current shot description
static T_FrameWrite      I_FrameWrite     ; // A010 P RW   FrameWrite Use this to change a single frame in the current shot description
static T_WaterLevels     I_WaterLevels    ; // A011 Q RW   WaterLevels Use this to adjust and read water level settings
static T_Calibration     I_Calibration    ; // A012 R RW   Calibration Use this to adjust and read calibration
"""
class Tester(object):
  """
  Test BLE operations on the DE1
  """
  def __init__(self, target):
    EP = EntryParser('MemMap.def')
    self.ByUUID = {}
    for (k,v) in sorted(EP.Entries.items()):
      self.ByUUID[v[0]] = (k, v[2], v[3], v[4])
      
    self.p = Peripheral(target, ADDR_TYPE_RANDOM)
    self.p.withDelegate(NotifyDelegate)
    
    service = self.p.getServiceByUUID('a000')
    print( "Service UUID: ", service.uuid.getCommonName().upper())
    ch = service.getCharacteristics()
    self.Chars = {}
    for j in ch:
      cn = j.uuid.getCommonName().upper()
      desc = self.ByUUID[int(cn, 16)]
      print( "\t", cn, desc[0], "%2s" % desc[1], "%20s:" % desc[2], desc[3])
      self.Chars[cn] = j

    self.FWReadFromMMR = self.Chars['A005']
    self.FWWriteToMMR  = self.Chars['A006']
    self.FWMapRequest = self.Chars['A009']
    self.HeaderWrite = self.Chars['A00F']
    self.FrameWrite = self.Chars['A010']

    self.resp_ReadFromMMR = []
    self.resp_ReadFromMMRExpected = 0

    registerForNotifyCallback(self.FWReadFromMMR, self.callback_FWReadFromMMR, 'FWReadFromMMR')
    registerForNotifyCallback(self.FWWriteToMMR, self.callback_FWWriteToMMR, 'FWWriteToMMR')
    
  def writeToMMR(self, data, address, withResponse=True):
    try:
      write_FWWriteToMMR(self.FWWriteToMMR, data, address, withResponse)
    except BTLEException as err:
      print()
      print('BLE error: ', err)
      traceback.print_exc()

  def readFromMMR(self, dbytes, address, withResponse=True):
    dlen = (dbytes//4) - 1
    try:
      write_FWReadFromMMR(self.FWReadFromMMR, dlen, address, withResponse)
      self.resp_ReadFromMMRExpected += 1
    except BTLEException as err:
      print()
      print('BLE error: ', err)
      traceback.print_exc()      


  def callback_FWWriteToMMR(self, params, data):
    print("%s: %s" % (params, ":".join("{:02x}".format(ord(c)) for c in data)))
    (dlen, addr, data) = struct.unpack('>B3s%ds' % (len(data)-5), data)
    print("%d %08X %32X", fromU8P0(dlen), fromU24P0(addr), data)

  def callback_FWReadFromMMR(self, params, data):
    #print("%s: %s" % (params, ":".join("{:02x}".format(ord(c)) for c in data)))
    (dlen, addr, data) = struct.unpack('>B3s%ds' % (len(data)-4), data)
    addr = fromU24P0(addr)
    self.resp_ReadFromMMR.append( (dlen, addr, data[0:dlen]) )
    #print("%d %08X %s" % (dlen, addr, hexDump(data[0:dlen])))
    # if (dlen == 4):
    #   print("%d %08X %s %d" % (dlen, addr, hexDump(data[0:dlen]), fromU32P0LE(data[0:dlen])))
    # else:
    #   print("%d %08X %s" % (dlen, addr, hexDump(data[0:dlen])))
    
    
  def readMMRWithResponse(self, bytecnt, addr):
    self.resp_ReadFromMMR = []
    self.resp_ReadFromMMRExpected = 0
    expected = (bytecnt+15)//16
    self.readFromMMR(bytecnt, addr)
    while (len(self.resp_ReadFromMMR) < expected):
      self.p.waitForNotifications(1) 

    result = array('B', '\0'*bytecnt)
    for (dlen, daddr, data) in self.resp_ReadFromMMR:
      result[daddr-addr : daddr-addr+dlen] = array('B', data[0:dlen])

    return result


  def readDebugOutput(self):
    data = self.readMMRWithResponse(4, 0x802800)
    print(repr(data))
    bytecnt = struct.unpack('<I', data)[0]
    print("Bytecnt: ", bytecnt)

    addr = 0x802804
    result = array('B')
    while (bytecnt > 0):
      if bytecnt >= 1024:
        data = self.readMMRWithResponse(1024, addr)
        bytecnt -= 1024
        addr += 1024
        result.extend(data)
      else:
        data = self.readMMRWithResponse(bytecnt, addr)
        addr += bytecnt
        bytecnt = 0
        result.extend(data)

    result = result.tostring()
    data = self.readMMRWithResponse(4, 0x803804)
    print(result)

  def testMMRReads(self):
    # self.p.waitForNotifications(200)
    resp = read_FWMapRequest(self.FWMapRequest)
    self.readFromMMR(4, 0x800000)
    self.readFromMMR(4, 0x800004)
    self.readFromMMR(4, 0x800008)
    self.readFromMMR(4, 0x80000C)
    self.readFromMMR(4, 0x800010)
    self.readFromMMR(16, 0x800000)
    while (len(self.resp_ReadFromMMR) < self.resp_ReadFromMMRExpected):
      self.p.waitForNotifications(1)  

    self.readDebugOutput()

    resp = read_FWMapRequest(self.FWMapRequest)
    
  def testFlowCalEst(self):
    data = struct.pack("<I", int(0.8*1000))
    #padded = struct.pack("4s12s", data, b'\0'*12)
    #unpadded = struct.pack("4s", data)
    self.writeToMMR(data, 0x80383C, withResponse=False)
    for i in range(3):
      self.p.waitForNotifications(1)

  def testProfileWrite(self):
    # Write to HeaderDesc
    # Write to Frames
    # Write extended Frames
    # Write to ShotTail
    write_ShotDescHeader(self.HeaderWrite, 3, 1, withResponse=False)
    sleep(0.1)
    #write_ShotFrameToFrameWrite(ctic, FrameNum, Flags, SetVal, Temp, FrameLen, TriggerVal, MaxVol, withResponse=True):

    write_ShotFrameToFrameWrite(self.FrameWrite, 0,  0x7, 2.0, 40, 15.0, 0.5, 100.0, withResponse=False)   
    sleep(0.1)
    write_ShotFrameToFrameWrite(self.FrameWrite, 1,  0x0, 4.0, 40, 15.0,   0, 100.0, withResponse=False)
    sleep(0.1)
    write_ShotFrameToFrameWrite(self.FrameWrite, 2, 0x20, 2.0, 40, 15.0,   0, 100.0, withResponse=False)
    sleep(0.1)

    #write_ShotExtFrameToFrameWrite(ctic, FrameNum, MaxFlowOrPressure, MaxFoPRange, withResponse=True):
    write_ShotExtFrameToFrameWrite(self.FrameWrite, 2+32, 1.0, 1.0, withResponse=False)
    sleep(0.1)

    #write_ShotTailToFrameWrite(ctic, MaxTotalVolume, UsePI, withResponse=True):
    write_ShotTailToFrameWrite(self.FrameWrite, 3, 200.0, True, withResponse=False)
    sleep(0.1)



    pass

if __name__=="__main__":
  de1s = Scan.find_DE1()
  
  if len(de1s) == 0:
    print("No DE1s found")
  else:
    de1 = list(sorted(de1s))[0][1]
    print("Using {}".format(de1))
    
    T = Tester(de1)
    #T.testMMRReads()
    #T.testProfileWrite()
    T.testFlowCalEst()
    