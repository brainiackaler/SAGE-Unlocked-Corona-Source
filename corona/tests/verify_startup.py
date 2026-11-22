from pathlib import Path
import struct
import sys
from unicorn import Uc,UC_ARCH_X86,UC_MODE_32
from unicorn.x86_const import *

ROOT = Path(sys.argv[1]) if len(sys.argv) > 1 else Path(__file__).resolve().parent
STUB,SETTER,NET,DATA,VIEW,STACK,CLOCK,SYSTEM=(0x100000,0x200000,0x900000,0xA00000,0xB00000,0xD00000,0xE00000,0xF00000)
u32=lambda n:struct.pack('<I',n)
f32=lambda n:struct.pack('<f',n)

def run(kind,frame=3,network=0,pending=1,mode=0,locked=1,height=550,attempts=0,foreign=False,null_clock=False):
    cpu=Uc(UC_ARCH_X86,UC_MODE_32)
    for a in (STUB,SETTER,NET,DATA,VIEW,STACK,CLOCK,SYSTEM):cpu.mem_map(a,0x4000)
    code=ROOT.joinpath('reset-stub.bin' if kind=='reset' else 'startup-stub.bin').read_bytes()
    cpu.mem_write(STUB,code)
    # The real setter is stdcall(this,float). A deliberately clobbered SIMD/x87 state tests preservation.
    setter=bytes.fromhex('8B 44 24 04 89 41 44 0F 57 C0 D9 EE C2 04 00')
    if kind=='reset':setter=bytes.fromhex('D9 05')+u32(DATA+12)+b'\xC3'
    cpu.mem_write(SETTER,setter)
    cpu.mem_write(STUB+0x800,bytes.fromhex('5E 5B 59 C3'))
    cpu.mem_write(DATA+4,f32(1.5));cpu.mem_write(DATA+8,u32(VIEW+4 if foreign else VIEW))
    cpu.mem_write(DATA+12,f32(550));cpu.mem_write(DATA+16,f32(825))
    cpu.mem_write(DATA+0x50,u32(pending));cpu.mem_write(DATA+0x70,u32(attempts))
    cpu.mem_write(VIEW+0x44,f32(height));cpu.mem_write(VIEW+0x48,bytes([locked]))
    cpu.mem_write(VIEW+0x2594,u32(mode));cpu.mem_write(NET,u32(network))
    cpu.mem_write(CLOCK,u32(0 if null_clock else SYSTEM));cpu.mem_write(SYSTEM+0x50,u32(frame))
    cpu.mem_write(DATA+0x100,f32(17.25))
    entry=STUB+0xC00
    h=bytes.fromhex('D9 05')+u32(DATA+0x100)
    h+=b'\xE8'+u32((STUB-(entry+len(h)+5))&0xffffffff)
    h+=bytes.fromhex('D9 1D')+u32(DATA+0x104)
    cpu.mem_write(entry,h)
    regs={UC_X86_REG_EAX:0x12345678,UC_X86_REG_EBX:0x23456789,UC_X86_REG_ECX:VIEW,
          UC_X86_REG_EDX:SETTER,UC_X86_REG_ESI:VIEW if kind=='reset' else 0x56789ABC,
          UC_X86_REG_EDI:0x6789ABCD,UC_X86_REG_EBP:0x789ABCDE,UC_X86_REG_ESP:STACK+0x1800,UC_X86_REG_EFLAGS:0x247}
    cpu.reg_write(UC_X86_REG_FPCW,0x37f)
    for r,v in regs.items():cpu.reg_write(r,v)
    xmm={r:0x123456789ABCDEF0123456789ABCDEF0+r for r in range(UC_X86_REG_XMM0,UC_X86_REG_XMM7+1)}
    for r,v in xmm.items():cpu.reg_write(r,v)
    cpu.emu_start(entry,entry+len(h),count=2000)
    for r,v in regs.items():assert cpu.reg_read(r)==v,(kind,r,hex(cpu.reg_read(r)),hex(v))
    for r,v in xmm.items():assert cpu.reg_read(r)==v,('SIMD corrupted',r)
    assert struct.unpack('<f',cpu.mem_read(DATA+0x104,4))[0]==17.25,'x87 stack changed'
    val=lambda a:struct.unpack('<I',cpu.mem_read(a,4))[0]
    height=struct.unpack('<f',cpu.mem_read(VIEW+0x44,4))[0]
    return height,val(DATA+0x50),val(DATA+0x60),val(DATA+0x70),struct.unpack('<f',cpu.mem_read(DATA+16,4))[0]

assert run('reset',pending=0)==(550.,1,0,0,825.)
assert run('reset',network=1,pending=0)==(550.,0,0,0,825.)
assert run('frame')==(825.,0,1,1,825.)
for args in [dict(frame=0),dict(frame=2),dict(network=1),dict(mode=4),dict(locked=0),dict(foreign=True),dict(null_clock=True)]:
    assert run('frame',**args)==(550.,1,0,0,825.),args
assert run('frame',pending=0,height=700)==(700.,0,0,0,825.),'manual zoom was overwritten'
assert run('frame',attempts=30)==(550.,0,0,30,825.),'failed setter keeps retrying forever'
print('PASS: reset only arms startup; waits until logic frame 3; applies once via native setter')
print('PASS: main menu/loading/cinematic/network/wrong view/null clock do not apply; manual zoom remains free')
print('PASS: retry limit, registers, flags, stack, x87 and all XMM registers preserved')

expiry_cpu = Uc(UC_ARCH_X86, UC_MODE_32)
for address in (STUB, CLOCK, STACK):
    expiry_cpu.mem_map(address, 0x1000)
expiry_cpu.mem_write(STUB, ROOT.joinpath('particle-expiry-stub.bin').read_bytes())
for clock_ms, remaining_ms in ((0, 0), (28, 0), (29, 0), (30, 1), (2000, 1971)):
    expiry_cpu.mem_write(CLOCK, u32(clock_ms))
    expiry_cpu.mem_write(STACK, u32(STUB + 0x100))
    expiry_cpu.reg_write(UC_X86_REG_ESP, STACK)
    expiry_cpu.emu_start(STUB, STUB + 0x100, count=20)
    assert expiry_cpu.reg_read(UC_X86_REG_EAX) == remaining_ms, clock_ms
    assert expiry_cpu.reg_read(UC_X86_REG_ESP) == STACK + 4
print('PASS: particle cleanup uses the adjusted clock and clamps startup time to zero')
