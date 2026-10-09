-- File identity/metadata snapshots are cheap guards after the startup hash.
-- The installer cannot transact while the game runs; a changed snapshot disarms.
local accepted
local function file_stamp(runtime,path)
    if runtime.context_file_stamp then return runtime.context_file_stamp(path)end
    local ffi=require('ffi')
    ffi.cdef[[int __stdcall GetFileAttributesExW(const unsigned short *, int, void *);]]
    local kernel=ffi.load('kernel32')
    local count=kernel.MultiByteToWideChar(65001,8,path,#path,nil,0)
    assert(count>0 and count<32768,'CONTEXT_REFUSED: file path encoding')
    local wide=ffi.new('unsigned short[?]',count+1)
    assert(kernel.MultiByteToWideChar(65001,8,path,#path,wide,count)==count,'CONTEXT_REFUSED: file path conversion')
    local data=ffi.new('unsigned long[9]')
    assert(ffi.sizeof(data)==36,'CONTEXT_REFUSED: native file metadata size')
    assert(kernel.GetFileAttributesExW(wide,0,data)~=0,'CONTEXT_UNAVAILABLE: file metadata')
    assert(math.floor(tonumber(data[0])/16)%2==0,'CONTEXT_REFUSED: expected file')
    return ffi.string(data,36)
end
function M.file_stamp_for_tests(runtime,path)return file_stamp(runtime,path)end
function M.require(runtime)
    local observed,paths=read_context(runtime)
    if not paths then accepted={runtime=runtime,observed=observed};return observed end
    local stamps={}
    for _,path in ipairs(paths.files)do stamps[path]=file_stamp(runtime,path)end
    accepted={runtime=runtime,observed=observed,stamps=stamps,pending=paths.pending}
    return observed
end
function M.recheck(runtime)
    assert(accepted and accepted.runtime==runtime,'CONTEXT_REFUSED: startup not accepted')
    if runtime.comparison_context then
        local config,receipt,pending=runtime.comparison_context()
        return M.check(config,receipt,pending)
    end
    for path,stamp in pairs(accepted.stamps)do
        assert(file_stamp(runtime,path)==stamp,'CONTEXT_REFUSED: installed file metadata changed')
    end
    local plan=io.open(accepted.pending,'rb')
    if plan then plan:close();error('CONTEXT_REFUSED: pending installation',0)end
    return accepted.observed
end
return M
