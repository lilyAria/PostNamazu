﻿using System;
using System.Collections.Generic;
using PostNamazu.Attributes;
using PostNamazu.Models;
using Newtonsoft.Json;
using PostNamazu.Common;

namespace PostNamazu.Actions
{
    internal class Preset : NamazuModule
    {
        private IntPtr UIModulePtrPtr;
        private Int32 WayMarkSlotOffset;
        public IntPtr MapIDPtr;

        public override void GetOffsets()
        {
            base.GetOffsets();
            UIModulePtrPtr = SigScanner.GetStaticAddressFromSig("48 8B 05 ?? ?? ?? ?? 48 8B D9 8B 40 14 85 C0");

            var mapIDOffset = SigScanner.Read<UInt16>(SigScanner.ScanText("66 89 81 ?? ?? ?? ?? 48 8D 0D ?? ?? ?? ?? E8 ?? ?? ?? ?? 44 8B CF") + 3);
            MapIDPtr = SigScanner.GetStaticAddressFromSig("48 8D 0D ?? ?? ?? ?? 0F B6 55 ??") + mapIDOffset;

            GetWayMarkSlotOffset();
        }

        private void GetWayMarkSlotOffset()
        {
            var UIModuleSwitch = new SwitchParser(SigScanner, "8B 94 98 ?? ?? ?? ?? 48 03 D0", 3);

            //.text:000000014063AE36                        loc_14063AE36:; CODE XREF: sub_14063ACD0 + 32↑j
            //.text:000000014063AE36
            //.text:000000014063AE36 49 8B 00                               mov rax, [r8]; jumptable 000000014063AD02 case 17
            //.text:000000014063AE39 49 8B C8                               mov rcx, r8
            //.text:000000014063AE3C 48 83 C4 20                            add rsp, 20h
            //.text:000000014063AE40 5B                                     pop     rbx
            //.text:000000014063AE41 48 FF A0 70 01 00 00                   jmp qword ptr[rax + 170h]
            var Case0x11 = UIModuleSwitch.Case(0x11);
            var offset = SigScanner.Read<int>(Case0x11 + 14);

            var UIModulePtr = SigScanner.Read<IntPtr>(UIModulePtrPtr);
            var UIModule = SigScanner.Read<IntPtr>(UIModulePtr);
            var FastCallAddressPtr = SigScanner.Read<IntPtr>(UIModule) + offset;

            var FastCallAddress = SigScanner.Read<IntPtr>(FastCallAddressPtr);
            //.text:00000001405BA9A0
            //.text:00000001405BA9A0                        sub_1405BA9A0   proc near; DATA XREF: .rdata: 0000000141649E10↓o
            //.text:00000001405BA9A0 48 8D 81 30 05 09 00                   lea rax, [rcx + 90530h]
            //.text:00000001405BA9A7 C3                                     retn
            //.text:00000001405BA9A7                        sub_1405BA9A0   endp
            WayMarkSlotOffset = SigScanner.Read<Int32>(FastCallAddress + 3);
        }
        public IntPtr GetWaymarkDataPointerForSlot(uint slotNum)
        {
            //var g_Framework_2 = MemoryService.Read<IntPtr>(g_Framework_2_Ptr);
            //var UIModule = MemoryService.Read<IntPtr>(g_Framework_2 + 0x29F8);
            var UIModulePtr = SigScanner.Read<IntPtr>(UIModulePtrPtr);
            var UIModule = SigScanner.Read<IntPtr>(UIModulePtr);

            var WayMarkSlotPtr = UIModule + WayMarkSlotOffset;
            var WaymarkDataPointer = WayMarkSlotPtr + 64 + (int)(104 * (slotNum - 1));
            //logger.Debug($"WaymarkDataPointer={WaymarkDataPointer.ToUint64().AsHex()}");
            return WaymarkDataPointer;
        }

        /// <summary>
        ///     载入预设
        /// </summary>
        /// <param name="waymarks">标点合集对象</param>
        private void DoInsertPreset(WayMarks waymarks)
        {
            ushort mapID = (waymarks.MapID is > 1000 or 0) ? SigScanner.Read<ushort>(MapIDPtr) : waymarks.MapID;
            if (mapID==0)
            {
                Log("预设与当前地图ID均不合法，加载预设失败");
                return;
            }

            IntPtr SlotOffset = waymarks.Name switch
            {
                "Slot1" => GetWaymarkDataPointerForSlot(1),
                "Slot2" => GetWaymarkDataPointerForSlot(2),
                "Slot3" => GetWaymarkDataPointerForSlot(3),
                "Slot4" => GetWaymarkDataPointerForSlot(4),
                "Slot5" => GetWaymarkDataPointerForSlot(5),
                _ => GetWaymarkDataPointerForSlot(1)
            };

            byte[] importdata = ConstructGamePreset(waymarks, mapID);
            Memory.WriteBytes(SlotOffset, importdata);
            
        }
        /// <summary>
        ///     载入预设
        /// </summary>
        /// <param name="waymarksStr">标点合集序列化Json字符串</param>
        [Command("preset")] [Command("DoInsertPreset")]
        public void DoInsertPreset(string waymarksStr)
        {
            if (!isReady) {
                PluginUI.Log("执行错误：接收到指令，但是没有对应的游戏进程");
                throw new Exception("没有对应的游戏进程");
            }

            if (waymarksStr == "")
                throw new Exception("指令为空");

            switch (waymarksStr.ToLower()) {
                default:
                    var waymarks = JsonConvert.DeserializeObject<WayMarks>(waymarksStr);
                    PluginUI.Log(waymarksStr);
                    DoInsertPreset(waymarks);
                    break;
            }
        }
        /// <summary>
        ///     构造预设结构，从0号头子的PPR抄来的
        /// </summary>
        /// <param name="waymark">标点</param>
        /// <returns>byte[]预设结构</returns>
        public byte[] ConstructGamePreset(WayMarks waymarks, ushort MapID)
        {
            //	List is easy because we can just push data on to it.
            List<byte> byteData = new List<byte>();
            byte activeMask = 0x00;
            foreach (var twaymark in waymarks)
            {
                Waymark waymark = twaymark ?? new Waymark();
                byteData.AddRange(BitConverter.GetBytes(waymark.Active ? (Int32)(waymark.X * 1000.0) : 0));
                byteData.AddRange(BitConverter.GetBytes(waymark.Active ? (Int32)(waymark.Y * 1000.0) : 0));
                byteData.AddRange(BitConverter.GetBytes(waymark.Active ? (Int32)(waymark.Z * 1000.0) : 0));
                activeMask >>= 1;
                if (waymark.Active) activeMask |= 0b10000000;
            }

            byteData.Add(activeMask);

            //	Reserved byte.
            byteData.Add((byte)0x00);

            //	Territory ID.
            byteData.AddRange(BitConverter.GetBytes(MapID));

            //	Time last modified.
            DateTimeOffset Time = new DateTimeOffset(DateTimeOffset.Now.UtcDateTime);
            byteData.AddRange(BitConverter.GetBytes((Int32)Time.ToUnixTimeSeconds()));

            //	Shouldn't ever come up with the wrong length, but just in case...
            if (byteData.Count != 104)
            {
                throw new Exception("Error in ConstructGamePreset(): Constructed byte array was of an unexpected length.");
            }

            //	Send it out.
            return byteData.ToArray();
        }
    }
}
