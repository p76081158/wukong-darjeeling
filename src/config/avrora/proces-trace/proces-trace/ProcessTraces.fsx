#load "Datatypes.fsx"
#load "Helpers.fsx"
#load "AVR.fsx"
#load "JVM.fsx"
#load "ResultsToString.fsx"

open System
open System.IO
open System.Linq
open System.Text.RegularExpressions
open System.Runtime.Serialization
open Datatypes
open Helpers
open ResultsToString

// The infusion header isn't necessary anymore now that Avrora will put the method name in
// rtcdata.xml, but let's keep it around in case it comes in handy later.
//type DarjeelingInfusionHeaderXml = XmlProvider<"infusionheader-example.dih", Global=true>
//type Dih = DarjeelingInfusionHeaderXml.Dih

let JvmInstructionFromXml (xml : RtcdataXml.JavaInstruction) =
    {
        JvmInstruction.index = xml.Index
        text = Uri.UnescapeDataString(xml.Text)
    }
let AvrInstructionFromXml (xml : RtcdataXml.AvrInstruction) =
    let opcode = Convert.ToInt32((xml.Opcode.Trim()), 16)
    {
        AvrInstruction.address = Convert.ToInt32(xml.Address.Trim(), 16)
        opcode = opcode
        text = Uri.UnescapeDataString(xml.Text)
    }

// Input: the optimised avr code, and a list of tuples of unoptimised avr instructions and the jvm instruction that generated them
// Returns: a list of tuples (optimised avr instruction, unoptimised avr instruction, corresponding jvm index)
//          the optimised avr instruction may be None for instructions that were removed completely by the optimiser
let rec matchOptUnopt (optimisedAvr : AvrInstruction list) (unoptimisedAvr : (AvrInstruction*JvmInstruction) list) =
    let isAOT_PUSH x = AVR.PUSH.is (x.opcode) || AVR.ST_XINC.is (x.opcode) // AOT uses 2 stacks
    let isAOT_POP x = AVR.POP.is (x.opcode) || AVR.LD_DECX.is (x.opcode)
    let isMOV x = AVR.MOV.is (x.opcode)
    let isMOVW x = AVR.MOVW.is (x.opcode)
    let isBREAK x = AVR.BREAK.is (x.opcode)
    let isNOP x = AVR.NOP.is (x.opcode)
    let isJMP x = AVR.JMP.is (x.opcode)
    let isRJMP x = AVR.RJMP.is (x.opcode)
    let isBRANCH x = AVR.BREQ.is (x.opcode) || AVR.BRGE.is (x.opcode) || AVR.BRLO.is (x.opcode) || AVR.BRLT.is (x.opcode) || AVR.BRNE.is (x.opcode) || AVR.BRSH.is (x.opcode)
    let isBRANCH_BY_BYTES x y = isBRANCH x && ((((x.opcode) &&& (0x03F8)) >>> 2) = y) // avr opcode BRxx 0000 00kk kkkk k000, with k the offset in WORDS (thus only shift right by 2, not 3, to get the number of bytes)

    let isMOV_MOVW_PUSH_POP x = isMOV x || isMOVW x || isAOT_PUSH x || isAOT_POP x
    match optimisedAvr, unoptimisedAvr with
    // Identical instructions: match and consume both
    | optimisedHead :: optimisedTail, (unoptimisedHead, jvmHead) :: unoptTail when optimisedHead.text = unoptimisedHead.text
        -> (Some optimisedHead, unoptimisedHead, jvmHead) :: matchOptUnopt optimisedTail unoptTail
    // Match a MOV to a single PUSH instruction (bit arbitrary whether to count the cycle for the PUSH or POP that was optimised)
    | optMOV :: optTail, (unoptPUSH, jvmHead) :: unoptTail when isMOV(optMOV) && isAOT_PUSH(unoptPUSH)
        -> (Some optMOV, unoptPUSH, jvmHead)
            :: matchOptUnopt optTail unoptTail
    // Match a MOVW to two PUSH instructions (bit arbitrary whether to count the cycle for the PUSH or POP that was optimised)
    | optMOVW :: optTail, (unoptPUSH1, jvmHead1) :: (unoptPUSH2, jvmHead2) :: unoptTail when isMOVW(optMOVW) && isAOT_PUSH(unoptPUSH1) && isAOT_PUSH(unoptPUSH2)
        -> (Some optMOVW, unoptPUSH1, jvmHead1)
            :: (None, unoptPUSH2, jvmHead2)
            :: matchOptUnopt optTail unoptTail
    // If the unoptimised head is a MOV PUSH or POP, skip it
    | _, (unoptimisedHead, jvmHead) :: unoptTail when isMOV_MOVW_PUSH_POP(unoptimisedHead)
        -> (None, unoptimisedHead, jvmHead) :: matchOptUnopt optimisedAvr unoptTail
    // BREAK signals a branchtag that would have been replaced in the optimised code by a
    // branch to the real address, possibly followed by one or two NOPs
    | _, (unoptBranchtag, jvmBranchtag) :: (unoptBranchtag2, jvmBranchtag2) :: unoptTail when isBREAK(unoptBranchtag)
        -> match optimisedAvr with
            // Short conditional jump, followed by a JMP or RJMP which was generated by a GOTO -> only the BR should match with the current JVM instruction
            | optBR :: optJMPRJMP :: optNoJMPRJMP :: optTail when isBRANCH(optBR) && (isJMP(optJMPRJMP) || isRJMP(optJMPRJMP)) && not (isJMP(optNoJMPRJMP) || isRJMP(optNoJMPRJMP)) && isBREAK(fst(unoptTail |> List.head))
                -> (Some optBR, unoptBranchtag, jvmBranchtag)
                    :: matchOptUnopt (optJMPRJMP :: optNoJMPRJMP :: optTail) unoptTail
            // Long conditional jump (branch by 2 or 4 bytes to jump over the next RJMP or JMP)
            | optBR :: optJMP :: optTail when ((isBRANCH_BY_BYTES optBR 2) || (isBRANCH_BY_BYTES optBR 4)) && isJMP(optJMP)
                -> (Some optBR, unoptBranchtag, jvmBranchtag)
                    :: (Some optJMP, unoptBranchtag, jvmBranchtag)
                    :: matchOptUnopt optTail unoptTail
            // Mid range conditional jump (branch by 2 or 4 bytes to jump over the next RJMP or JMP)
            | optBR :: optRJMP :: optTail when ((isBRANCH_BY_BYTES optBR 2) || (isBRANCH_BY_BYTES optBR 4)) && isRJMP(optRJMP)
                -> (Some optBR, unoptBranchtag, jvmBranchtag)
                    :: (Some optRJMP, unoptBranchtag, jvmBranchtag)
                    :: matchOptUnopt optTail unoptTail
            // Short conditional jump
            | optBR :: optTail when isBRANCH(optBR)
                -> (Some optBR, unoptBranchtag, jvmBranchtag)
                    :: matchOptUnopt optTail unoptTail
            // Uncondtional long jump
            | optJMP :: optTail when isJMP(optJMP)
                -> (Some optJMP, unoptBranchtag, jvmBranchtag)
                    :: matchOptUnopt optTail unoptTail
            // Uncondtional mid range jump
            | optRJMP :: optTail when isRJMP(optRJMP)
                -> (Some optRJMP, unoptBranchtag, jvmBranchtag)
                    :: matchOptUnopt optTail unoptTail
            | head :: tail -> failwith ("Incorrect branchtag @ address " + head.address.ToString() + ": " + head.text + " / " + unoptBranchtag2.text)
            | _ -> failwith "Incorrect branchtag"
    | [], [] -> [] // All done.
    | head :: tail, [] -> failwith ("Some instructions couldn't be matched(1): " + head.text)
    | [], (head, jvm) :: tail -> failwith ("Some instructions couldn't be matched(2): " + head.text)
    | head1 :: tail1, (head2, jvm) :: tail2 -> failwith ("Some instructions couldn't be matched: " + head1.address.ToString() + ":" + head1.text + "   +   " + head2.address.ToString() + ":" + head2.text)

// Input: the original Java instructions, trace data from avrora profiler, and the output from matchOptUnopt
// Returns: a list of ResultJava records, showing the optimised code per original JVM instructions, and amount of cycles spent per optimised instruction
let addCountersAndDebugData (jvmInstructions : JvmInstruction list) (countersForAddressAndInst : int -> int -> ExecCounters) (matchedResults : (AvrInstruction option*AvrInstruction*JvmInstruction) list) (possiblyEmptyDjDebugDatas : DJDebugData list) =
    let djDebugDatas = match possiblyEmptyDjDebugDatas with
                       | [] -> jvmInstructions |> List.map (fun x -> DJDebugData.empty) // If we don't have debug data, just use a list of empty DJDebugDatas of equal length as jvmInstructions
                       | _ -> (DJDebugData.empty :: possiblyEmptyDjDebugDatas) // Add empty line to match the preable in rtcdata.xml
    List.zip jvmInstructions djDebugDatas // Add an empty entry to debug data to match the method preamble
        |> List.map
        (fun (jvm, debugdata) ->
            let resultsForThisJvm = matchedResults |> List.filter (fun b -> let (_, _, jvm2) = b in jvm.index = jvm2.index) in
            let resultsWithCounters = resultsForThisJvm |> List.map (fun (opt, unopt, _) ->
                  let counters = match opt with
                                 | None -> ExecCounters.Zero
                                 | Some(optValue) -> countersForAddressAndInst optValue.address optValue.opcode
                  { unopt = unopt; opt = opt; counters = counters }) in
            let foldAvrCountersToJvmCounters (a : ExecCounters) (b : ExecCounters) =
                { (a+b) with executions = (if a.executions > 0 then a.executions else b.executions) }
            { jvm = jvm
              avr = resultsWithCounters
              counters = resultsWithCounters |> List.map (fun r -> r.counters) |> List.fold (foldAvrCountersToJvmCounters) ExecCounters.Zero
              djDebugData = debugdata })

let getTimersFromStdout (stdoutlog : string list) =
    let pattern = "Timer (\w+) ran (\d+) times for a total of (\d+) cycles."
    stdoutlog |> List.map (fun line -> Regex.Match(line, pattern))
              |> List.filter (fun regexmatch -> regexmatch.Success)
              |> List.map (fun regexmatch -> (regexmatch.Groups.[1].Value, Int32.Parse(regexmatch.Groups.[3].Value)))
              |> List.sortBy (fun (timer, cycles) -> match timer with "NATIVE" -> 1 | "AOT" -> 2 | "JAVA" -> 3 | _ -> 4)

let getNativeInstructionsFromObjdump (name : string) (objdumpOutput : string list) (countersForAddressAndInst : int -> int -> ExecCounters) =
    let pattern = "^[0-9a-fA-F]+ <" + name + ">:$"
    let startIndex = objdumpOutput |> List.findIndex (fun line -> Regex.IsMatch(line, pattern))
    let disasmTail = objdumpOutput |> List.skip (startIndex + 1)
    let endIndex = disasmTail |> List.findIndex (fun line -> Regex.IsMatch(line, "^[0-9a-fA-F]+ <.*>:$"))
    let disasm = disasmTail |> List.take endIndex |> List.filter ((<>) "")
    let pattern = "^\s*([0-9a-fA-F]+):((\s[0-9a-fA-F][0-9a-fA-F])+)\s+(\S.*)$"
    let regexmatches = disasm |> List.map (fun line -> Regex.Match(line, pattern))
    let avrInstructions = regexmatches |> List.map (fun regexmatch ->
        let opcodeBytes = regexmatch.Groups.[2].Value.Split(' ') |> Array.map (fun x -> Convert.ToInt32(x.Trim(), 16))
        let opcode = if (opcodeBytes.Length = 2)
                     then ((opcodeBytes.[1] <<< 8) + opcodeBytes.[0])
                     else ((opcodeBytes.[3] <<< 24) + (opcodeBytes.[2] <<< 16) + (opcodeBytes.[1] <<< 8) + opcodeBytes.[0])
        {
            AvrInstruction.address = Convert.ToInt32(regexmatch.Groups.[1].Value, 16)
            opcode = opcode
            text = regexmatch.Groups.[4].Value
        })
    avrInstructions |> List.map (fun avr -> (avr, countersForAddressAndInst avr.address avr.opcode))



let parseDJDebug (name : string) (allLines : string list) =
  let regexLine = Regex("^\s*(?<byteOffset>\d\d\d\d);[^;]*;[^;]*;(?<text>[^;]*);(?<stackBefore>[^;]*);(?<stackAfter>[^;]*);.*")
  let regexStackElement = Regex("^(?<byteOffset>\d+)(?<datatype>[a-zA-Z]+)$")

  let startIndex = allLines |> List.findIndex (fun line -> Regex.IsMatch(line, "^\s*method.*" + Regex.Escape(name) + "$"))
  let linesTail = allLines |> List.skip (startIndex + 3)
  let endIndex = linesTail |> List.findIndex (fun line -> Regex.IsMatch(line, "^\s*}\s*$"))
  let lines = linesTail
              |> List.take endIndex
              |> List.filter ((<>) "")
              |> List.filter (fun line -> not (line.Contains("lightweightmethodparameter"))) // These won't be in rtcdata since they don't generate any code.

  // System.Console.WriteLine (String.Join("\r\n", lines))

  let regexLineMatches = lines |> List.map (fun x -> regexLine.Match(x))
  let byteOffsetToInstOffset = regexLineMatches |> List.mapi (fun i m -> (Int32.Parse(m.Groups.["byteOffset"].Value.Trim()), i)) |> Map.ofList
  let stackStringToStack (stackString: string) =
      let split = stackString.Split(',')
                  |> Seq.toList
                  |> List.map (fun x -> match x.IndexOf('(') with // Some elements are in the form 20Short(Byte) to indicate Darjeeling knows the short value only contains a byte. Strip this information for now.
                                        | -1 -> x
                                        | index -> x.Substring(0, index))
                  |> List.map (fun x -> match x.IndexOf(':') with // Some elements are in the form 20Int:40Int to indicate a value on the stack may have come from either of two instructions. Strip this information too.
                                        | -1 -> x
                                        | index -> x.Substring(0, index))
                  |> List.filter ((<>) "")
      let regexElementMatches = split |> List.map (fun x -> regexStackElement.Match(x))
      // Temporarily just fill each index with 0 since the debug output from Darjeeling has a bug when instructions are replaced. The origin of each stack element is determined before replacing DUP and POP instructions.
      // So after replacing them, the indexes are no longer valid. Too much work to fix it properly in DJ for now. Will do that later if necessary.3
      // regexElementMatches |> List.map (fun m -> let byteOffset = Int32.Parse(m.Groups.["byteOffset"].Value) in
      //                                           let instOffset = if (Map.containsKey byteOffset byteOffsetToInstOffset)
      //                                                            then Map.find byteOffset byteOffsetToInstOffset
      //                                                            else failwith ("Key not found!!!! " + byteOffset.ToString() + " in " + string) in
      //                                           let datatype = (m.Groups.["datatype"].Value |> StackDatatypeFromString) in
      //                                           { StackElement.origin=instOffset; datatype=datatype })
      regexElementMatches |> List.map (fun m -> { StackElement.origin=0; datatype=(m.Groups.["datatype"].Value |> StackDatatypeFromString) })
  regexLineMatches |> List.mapi (fun i m -> { byteOffset = Int32.Parse(m.Groups.["byteOffset"].Value.Trim());
                                          instOffset = i;
                                          text = m.Groups.["text"].Value.Trim();
                                          stackBefore = (m.Groups.["stackBefore"].Value.Trim() |> stackStringToStack);
                                          stackAfter = (m.Groups.["stackAfter"].Value.Trim()  |> stackStringToStack) })

let processJvmMethod benchmark (methodImpl : MethodImpl) (countersForAddressAndInst : int -> int -> ExecCounters) (djdebuglines : string list) (addressesOfMathFunctions : (string * int) list) =
    printfn "Processing jvm method %s" (getClassAndMethodNameFromImpl methodImpl)
    let optimisedAvr = methodImpl.AvrInstructions |> Seq.map AvrInstructionFromXml |> Seq.toList
    let unoptimisedAvrWithJvmIndex =
        methodImpl.JavaInstructions |> Seq.map (fun jvm -> jvm.UnoptimisedAvr.AvrInstructions |> Seq.map (fun avr -> (AvrInstructionFromXml avr, JvmInstructionFromXml jvm)))
                                    |> Seq.concat
                                    |> Seq.toList

    let matchedResult = matchOptUnopt optimisedAvr unoptimisedAvrWithJvmIndex
    let djdebugdata = parseDJDebug (getClassAndMethodNameFromImpl methodImpl) djdebuglines
    let processedJvmInstructions = addCountersAndDebugData (methodImpl.JavaInstructions |> Seq.map JvmInstructionFromXml |> Seq.toList) countersForAddressAndInst matchedResult djdebugdata

    let countersPerJvmOpcodeAOTJava =
        processedJvmInstructions
            |> List.filter (fun r -> r.jvm.text <> "Method preamble")
            |> groupFold (fun r -> r.jvm.text.Split().First()) (fun r -> r.counters) (+) ExecCounters.Zero
            |> List.map (fun (opc, cnt) -> (JVM.getCategoryForJvmOpcode opc, opc, cnt))
            |> List.sortBy (fun (cat, opc, _) -> cat+opc)

    let countersPerAvrOpcodeAOTJava =
        processedJvmInstructions
            |> List.map (fun r -> r.avr)
            |> List.concat
            |> List.filter (fun avr -> avr.opt.IsSome)
            |> List.map (fun avr -> (AVR.getOpcodeForInstruction avr.opt.Value.opcode avr.opt.Value.text addressesOfMathFunctions, avr.counters))
            |> groupFold fst snd (+) ExecCounters.Zero
            |> List.map (fun (opc, cnt) -> (AVR.opcodeCategory opc, AVR.opcodeName opc, cnt))
            |> List.sortBy (fun (cat, opc, _) -> cat+opc)

    let codesizeJavaForAOT = methodImpl.JvmMethodSize
    let codesizeJavaBranchCount = methodImpl.BranchCount
    let codesizeJavaBranchTargetCount = methodImpl.BranchTargets |> Array.length
    let codesizeJavaMarkloopCount = methodImpl.MarkloopCount
    let codesizeJavaMarkloopTotalSize = methodImpl.MarkloopTotalSize
    let codesizeJavaWithoutBranchOverhead = codesizeJavaForAOT - (2*codesizeJavaBranchCount) - codesizeJavaBranchTargetCount
    let codesizeJavaWithoutBranchMarkloopOverhead = codesizeJavaForAOT - (2*codesizeJavaBranchCount) - codesizeJavaBranchTargetCount - codesizeJavaMarkloopTotalSize
    let codesizeJavaForInterpreter = codesizeJavaWithoutBranchMarkloopOverhead
    let codesizeAOT =
        let numberOfBranchTargets = methodImpl.BranchTargets |> Array.length
        methodImpl.AvrMethodSize - (4*numberOfBranchTargets) // We currently keep the branch table at the head of the method, but actually we don't need it anymore after code generation, so it shouldn't count for code size.

    {
        JvmMethod.name = (getClassAndMethodNameFromImpl methodImpl)
        instructions = processedJvmInstructions

        codesizeJavaForAOT = codesizeJavaForAOT
        codesizeJavaBranchCount = codesizeJavaBranchCount
        codesizeJavaBranchTargetCount = codesizeJavaBranchTargetCount
        codesizeJavaMarkloopCount = codesizeJavaMarkloopCount
        codesizeJavaMarkloopTotalSize = codesizeJavaMarkloopTotalSize
        codesizeJavaWithoutBranchOverhead = codesizeJavaWithoutBranchOverhead
        codesizeJavaWithoutBranchMarkloopOverhead = codesizeJavaWithoutBranchMarkloopOverhead
        codesizeJavaForInterpreter = codesizeJavaForInterpreter
        codesizeAOT = codesizeAOT

        countersPerJvmOpcodeAOTJava = countersPerJvmOpcodeAOTJava
        countersPerAvrOpcodeAOTJava = countersPerAvrOpcodeAOTJava
    }

let processCFunction (name : string) (countersForAddressAndInst : int -> int -> ExecCounters) (disasm : string list) (addressesOfMathFunctions : (string * int) list) =
    printfn "Processing c function %s" (name)

    let nativeCInstructions = getNativeInstructionsFromObjdump name disasm countersForAddressAndInst
    let countersPerAvrOpcodeNativeC =
        nativeCInstructions
            |> List.map (fun (avr, cnt) -> (AVR.getOpcodeForInstruction avr.opcode avr.text addressesOfMathFunctions, cnt))
            |> groupFold fst snd (+) ExecCounters.Zero
            |> List.map (fun (opc, cnt) -> (AVR.opcodeCategory opc, AVR.opcodeName opc, cnt))
            |> List.sortBy (fun (cat, opc, _) -> cat+opc)

    let codesizeC =
        let startAddress = (nativeCInstructions |> List.head |> fst).address
        let lastInList x = x |> List.reduce (fun _ x -> x)
        let endAddress = (nativeCInstructions |> lastInList |> fst).address
        endAddress - startAddress + 2 // assuming the function ends in a 2 byte opcode.

    {
        CFunction.name = name
        instructions = nativeCInstructions |> Seq.toList

        codesizeC = codesizeC

        countersPerAvrOpcodeNativeC = countersPerAvrOpcodeNativeC
    }

let parseNm (nm : string list) =
  // Possible formats:
  // 00000001 a __zero_reg__
  // 0000cc0a T _div /opt/local/var/macports/build/_opt_local_var_macports_sources_rsync.macports.org_release_tarballs_ports_cross_avr-gcc/avr-gcc/work/gcc-4.9.1/libgcc/config/avr/lib1funcs.S:1366
  // 00009a44 0000000c T asm_opcodeWithSingleRegOperand
  // 000099c8 00000058 T asm_guard_check_regs  /Users/nielsreijers/src/rtc/src/lib/rtc/c/arduino/asm_functions.c:15
  nm |> List.map (fun line ->
    let splitLine = line.Split [|' '; '\t'|] |> Array.toList
    match splitLine with
    | [ address; symbolType; name ] -> 
        {
          address = Convert.ToInt32(address, 16)
          size = 0
          symbolType = symbolType
          name = name
          file = ""
        }
    | [address; symbolType; name; file ] when file.StartsWith("/") ->
        {
          address = Convert.ToInt32(address, 16)
          size = 0
          symbolType = symbolType
          name = name
          file = file
        }
    | [address; size; symbolType; name ] -> 
        {
          address = Convert.ToInt32(address, 16)
          size = Convert.ToInt32(size, 16)
          symbolType = symbolType
          name = name
          file = ""
        }
    | [address; size; symbolType; name; file] -> 
        {
          address = Convert.ToInt32(address, 16)
          size = Convert.ToInt32(size, 16)
          symbolType = symbolType
          name = name
          file = file
        }
    | _ -> { address = 0; size = 0; symbolType = ""; name = line; file = "" })

let getINVOKEHelperAddressesFromJvmNm (jvmNm : NmData list) =
  let lookingFor = [ "RTC_INVOKESTATIC_FAST_NATIVE"; "RTC_INVOKESPECIAL_OR_STATIC_FAST_JAVA"; "RTC_INVOKEVIRTUAL_OR_INTERFACE" ]
  let addresses =  jvmNm |> List.filter (fun data -> lookingFor |> List.contains data.name)
                         |> List.map    (fun data -> data.address)
  if (lookingFor |> List.length) = (addresses |> List.length)
  then addresses
  else failwith "Not all INVOKEHelperAddresses found."

let mathFunctionNames = [ "__mulqi3"; "__mulqihi3"; "__umulqihi3"; "__mulhi3"; "__mulhisi3"; "__umulhisi3"; "__usmulhisi3"; "__mulshisi3"; "__muluhisi3"; "__mulohisi3"; "__mulsi3"; "__divmodqi4"; "__udivmodqi4"; "__divmodhi4"; "__udivmodhi4"; "__divmodsi4"; "__udivmodsi4"; "__fmul"; "__fmuls"; "__fmulsu" ]

let getBenchmarkFunctionNamesAndAddressesFromCNm (cNm : NmData list) =
  cNm |> List.filter (fun data ->
          data.symbolType.Equals("T", System.StringComparison.CurrentCultureIgnoreCase) // only select text symbols (code)
          && not (data.size=0)
          && match data.file with
             | "" ->
                // Unfortunately, avr-nm doesn't add the source file for some symbols. Just hard code what to do with those for now.
                let knownExclude = mathFunctionNames @ ["__do_clear_bss"; "avr_millis"; "__ultoa_invert"; "asm_opcodeWithSingleRegOperand"; "dj_exec_setVM"; "fputc"; "memcpy"; "memmove"; "memset"; "strnlen"; "strnlen_P"; "vfprintf"; "vsnprintf"]
                let knownInclude = ["siftDown"; "core_list_find"; "crc16"]
                match (knownInclude |> List.contains data.name, knownExclude |> List.contains data.name) with
                | (true, true)   -> failwith ("BUG in getBenchmarkFunctionNamesAndAddressesFromCNm. " + data.name + " can't be in both lists at the same time!")
                | (false, false) -> failwith ("Don't know whether to include " + data.name + ". Please put it in the include or exclude list in getBenchmarkFunctionNamesAndAddressesFromCNm")
                | (incl, _)      -> incl
             | file ->
                let knownExclude = ["Sinewave"; "stab"]                                      // Some symbols defined in benchmarks that aren't code.
                data.file.Contains("src/lib/bm_")                                            // only select functions in the benchmark lib
                && not ((data.name.StartsWith("bm_") && data.name.EndsWith("_init")))        // exclude the library's init function
                && not (data.name.Equals"javax_rtcbench_RTCBenchmark_void_test_native")      // exclude javax_rtcbench_RTCBenchmark_void_test_native (which contains benchmark init code)
                && not (knownExclude |> List.contains data.name))
      |> List.map (fun data -> (data.name, data.address))

let getMathFunctionAddressesFromNm (nm : NmData list) =
  let nmDatas = nm |> List.filter (fun data -> mathFunctionNames |> List.contains(data.name))
  nmDatas |> List.map (fun data -> (data.name, data.address))

let getCountersForSymbols (nmData : NmData list) (profilerdataPerAddress : (int * ProfiledInstruction) list) (symbols : string list) =
  let addresses = symbols |> List.map (fun symbol -> 
                                          let data = nmData |> List.find (fun data -> data.name.Equals(symbol))
                                          (data.name, data.address, data.address+data.size))
  let countersPerSymbol = addresses |> List.map (fun (name, fromAddress, toAddress) ->
                                                       (name,
                                                        profilerdataPerAddress |> List.filter (fun (address, _) -> fromAddress <= address && address < toAddress)
                                                                               |> List.sumBy (fun (_, p) ->
                                                                                  {
                                                                                      executions = p.Executions
                                                                                      cycles = p.Cycles                                        // Since this is a call to another benchmark function or method, don't count the subroutine cycles since we would end up counting them double
                                                                                      cyclesSubroutine = 0
                                                                                      count = 1
                                                                                      size = 0
                                                                                  })))
  // countersPerSymbol |> List.iter (fun (name, counters) ->
  //     printfn "hallo %s %d" name counters.cycles)
  countersPerSymbol |> List.sumBy (fun (_, counters) -> counters)


let getAllSymbolCounters (profilerdataPerAddress : (int * ProfiledInstruction) list) (nm : NmData list) =
  let profilerdataAsCounters = profilerdataPerAddress |> List.map (fun (address,profiledInstruction) ->
      (address, 
       {
          executions = profiledInstruction.Executions
          cycles = profiledInstruction.Cycles
          cyclesSubroutine = 0
          count = 1
          size = 0
       }))
  nm |> List.sortBy (fun nm -> nm.address)
     |> List.filter (fun nm -> nm.size > 0)
     |> List.map    (fun nm ->
        (nm.name, 
         profilerdataAsCounters |> List.filter (fun (addr,cnt) -> nm.address <= addr && addr < (nm.address + nm.size))
                                |> List.map (fun (_,cnt) -> cnt)
                                |> List.fold (+) ExecCounters.Zero))

let getCResultsdir (jvmResultsdir : string) =
  let indexOfLastSlash = jvmResultsdir.LastIndexOf("/");
  match (jvmResultsdir.Substring(indexOfLastSlash).StartsWith("/coremk")) with
  | false -> jvmResultsdir
  | true ->
    let indexOfSecondLastSlash = jvmResultsdir.LastIndexOf("/", indexOfLastSlash-1);
    let directoryForCoreMarkCResults = jvmResultsdir.Substring(0, indexOfSecondLastSlash) + "/results_coremk_c"
    directoryForCoreMarkCResults

let processSingleBenchmarkResultsDir (resultsdir : string) =
    let benchmark = (Path.GetFileName(resultsdir))

    let jvmResultsdir = resultsdir
    let jvmRtcdata = RtcdataXml.Load(String.Format("{0}/rtcdata.xml", jvmResultsdir))
    let jvmDjdebuglines = System.IO.File.ReadLines(String.Format("{0}/jlib_bm_{1}.debug", jvmResultsdir, benchmark)) |> Seq.toList
    let jvmProfilerdata = ProfilerdataXml.Load(String.Format("{0}/profilerdata.xml", jvmResultsdir)).Instructions |> Seq.toList
    let jvmStdoutlog = System.IO.File.ReadLines(String.Format("{0}/stdoutlog.txt", jvmResultsdir)) |> Seq.toList
    let jvmNm = parseNm (System.IO.File.ReadLines(String.Format("{0}/darjeeling.nm", jvmResultsdir)) |> Seq.toList)
    let jvmProfilerdataPerAddress = jvmProfilerdata |> List.map (fun x -> (Convert.ToInt32(x.Address.Trim(), 16), x))
    let jvmExcludeList = [ "RTCBenchmark.test_java" ]
    let jvmMethodsImpls = jvmRtcdata.MethodImpls |> Seq.filter (fun methodImpl -> (methodImpl.MethodDefInfusion.StartsWith("bm_")))
                                                 |> Seq.filter (fun methodImpl -> not (jvmExcludeList |> List.exists (fun ex -> (getClassAndMethodNameFromImpl methodImpl).Contains(ex)))) // Filter out the benchmark setup code
                                                 |> Seq.filter (fun methodImpl -> methodImpl.JavaInstructions.Length > 1) // Bug in RTC: abstract methods become just a method prologue
                                                 |> Seq.toList
    let jvmAddressesOfINVOKEHelperFunctions = getINVOKEHelperAddressesFromJvmNm jvmNm
    let jvmAddressesOfBenchmarkCalls = jvmMethodsImpls |> List.map (fun methodImpl -> Convert.ToInt32(methodImpl.StartAddress, 16))
    let jvmAddressesOfMathFunctions = getMathFunctionAddressesFromNm jvmNm

    let cResultsdir = (getCResultsdir resultsdir)
    let cProfilerdata = ProfilerdataXml.Load(String.Format("{0}/profilerdata.xml", cResultsdir)).Instructions |> Seq.toList
    let cStdoutlog = System.IO.File.ReadLines(String.Format("{0}/stdoutlog.txt", cResultsdir)) |> Seq.toList
    let cDisasm = System.IO.File.ReadLines(String.Format("{0}/darjeeling.S", cResultsdir)) |> Seq.toList
    let cNm = parseNm (System.IO.File.ReadLines(String.Format("{0}/darjeeling.nm", cResultsdir)) |> Seq.toList)
    let cProfilerdataPerAddress = cProfilerdata |> List.map (fun x -> (Convert.ToInt32(x.Address.Trim(), 16), x))
    let cBenchmarkFunctionNamesAndAddress = getBenchmarkFunctionNamesAndAddressesFromCNm cNm
    let cFunctionNames = cBenchmarkFunctionNamesAndAddress |> List.map fst
    let cAddressesOfBenchmarkCalls = cBenchmarkFunctionNamesAndAddress |> List.map snd
    let cAddressesOfMathFunctions = getMathFunctionAddressesFromNm cNm

    let countersForAddressAndInst (profilerdataPerAddress : (int * ProfiledInstruction) list) (addressesOfBenchmarkMethods : int list) address inst =
        match profilerdataPerAddress |> List.tryFind (fun (address2,inst) -> address = address2) with
        | None -> failwith (String.Format ("No profilerdata found for address {0}", address))
        | Some(_, profiledInstruction) ->
          match (addressesOfBenchmarkMethods |> List.contains (AVR.getTargetIfCALL inst)) with
          | true ->
            {
                executions = profiledInstruction.Executions
                cycles = profiledInstruction.Cycles                                        // Since this is a call to another benchmark function or method, don't count the subroutine cycles since we would end up counting them double
                cyclesSubroutine = profiledInstruction.CyclesSubroutine
                count = 1
                size = AVR.instructionSize inst
            }
          | false ->
            {
                executions = profiledInstruction.Executions
                cycles = (profiledInstruction.Cycles+profiledInstruction.CyclesSubroutine) // Since this is a call to somewhere outside the benchmark, count all cycles for this instruction
                cyclesSubroutine = 0
                count = 1
                size = AVR.instructionSize inst
            }    
    let jvmCountersForAddressAndInst = countersForAddressAndInst jvmProfilerdataPerAddress (jvmAddressesOfBenchmarkCalls @ jvmAddressesOfINVOKEHelperFunctions)
    let cCountersForAddressAndInst = countersForAddressAndInst cProfilerdataPerAddress cAddressesOfBenchmarkCalls

    let jvmMethods = jvmMethodsImpls |> List.map (fun methodImpl -> (processJvmMethod benchmark methodImpl jvmCountersForAddressAndInst jvmDjdebuglines jvmAddressesOfMathFunctions))

    let cFunctions = cFunctionNames |> List.map (fun name -> (processCFunction name cCountersForAddressAndInst cDisasm cAddressesOfMathFunctions))

    let getTimer stdoutlog timer =
        let stopwatchTimers = getTimersFromStdout stdoutlog
        match stopwatchTimers |> List.tryFind (fun (t,c) -> t=timer) with
        | Some(x) -> x |> snd
        | None -> 0


    let cyclesStopwatchAOT = (getTimer jvmStdoutlog "AOT")
    let cyclesStopwatchC = (getTimer cStdoutlog "NATIVE")

    let countersAOTVM =
        // let pathsVMFunctions = [ "src/lib/rtc"; "src/lib/vm"; "src/lib/wkreprog"; "src/lib/base"; "src/core" ]
        // let vmSymbols = jvmNm |> List.filter (fun nmData -> pathsVMFunctions |> List.exists (fun path -> nmData.file.Contains(path)))
        //                       |> List.map (fun nmData -> nmData.name)
        let vmSymbols = [ "RTC_INVOKEVIRTUAL_OR_INTERFACE"; "RTC_INVOKESPECIAL_OR_STATIC_FAST_JAVA"; "RTC_INVOKESTATIC_FAST_NATIVE"; "DO_INVOKEVIRTUAL"; "dj_object_getRuntimeId"; "dj_object_getReferences"; "dj_global_id_mapToInfusion"; "dj_global_id_lookupVirtualMethod"; "dj_vm_getRuntimeClassForInvoke"; "dj_exec_stackPeekDeepRef"; "callJavaMethod_setup"; "callJavaMethod"; "callNativeMethod"; "callMethodFast"; "callMethod"; "dj_infusion_getReferencedInfusionIndex"; "memset" ]
        { (getCountersForSymbols jvmNm jvmProfilerdataPerAddress vmSymbols) with executions = 0 }

    let cyclesSpentOnTimer  = if jvmResultsdir = cResultsdir // For coremark we have two sets of results, for others only 1. here we want the total cycles spent in the timer
                              then getCountersForSymbols jvmNm jvmProfilerdataPerAddress ["__vector_16"]
                              else getCountersForSymbols jvmNm jvmProfilerdataPerAddress ["__vector_16"] + getCountersForSymbols cNm cProfilerdataPerAddress ["__vector_16"]
    // Since we only hava a total, spread it evenly over AOT and C
    let fractionAOT x = (int ((float x) * ((float cyclesStopwatchAOT) / (float (cyclesStopwatchAOT+cyclesStopwatchC)))))
    let fractionC   x = (int ((float x) * ((float cyclesStopwatchC) / (float (cyclesStopwatchAOT+cyclesStopwatchC)))))
    let countersAOTTimer = 
            {
                executions       = fractionAOT cyclesSpentOnTimer.executions
                cycles           = fractionAOT cyclesSpentOnTimer.cycles
                cyclesSubroutine = fractionAOT cyclesSpentOnTimer.cyclesSubroutine
                count            = 0
                size             = 0
            }  
    let countersCTimer = 
            {
                executions       = fractionC cyclesSpentOnTimer.executions
                cycles           = fractionC cyclesSpentOnTimer.cycles
                cyclesSubroutine = fractionC cyclesSpentOnTimer.cyclesSubroutine
                count            = 0
                size             = 0
            }  

    let results = {
        benchmark = benchmark

        passedTestAOT = jvmStdoutlog |> List.exists (fun line -> line.Contains("RTC OK."))
        cyclesStopwatchAOT = cyclesStopwatchAOT
        cyclesStopwatchC = cyclesStopwatchC

        countersAOTVM = countersAOTVM
        countersAOTTimer = countersAOTTimer
        countersCTimer = countersCTimer

        jvmMethods = jvmMethods
        cFunctions = cFunctions

        jvmAllSymbolCounters = getAllSymbolCounters jvmProfilerdataPerAddress jvmNm
        cAllSymbolCounters   = getAllSymbolCounters cProfilerdataPerAddress cNm
    }

    let txtFilename = resultsdir + ".txt"
    File.WriteAllText (txtFilename, (resultsToString results))
    Console.Error.WriteLine ("Wrote output to " + txtFilename)

    let xmlFilename = resultsdir + ".xml"
    File.WriteAllText (xmlFilename, results.pickleToString)
    Console.Error.WriteLine ("Wrote output to " + xmlFilename)


let processConfigOrSingleBenchmarkResultsDir (directory) =
    // src/config/avrora/
    //     /results_0BASE_R___P__CS0
    //        /binsrch32
    //        /bsort32
    //        /..
    //     /results_1PEEP_R___P__CS0
    //        /binsrch32
    //        /bsort32
    //        /..
    //     /..

    // This function can be called for either a results_* directory to process all benchmark results,
    // or for a single benchmark directory like binsrch32, bsort32, etc.
    let subdirectories = (Directory.GetDirectories(directory))
    match subdirectories |> Array.length with
    | 0 -> processSingleBenchmarkResultsDir directory
    | _ -> subdirectories |> Array.iter processSingleBenchmarkResultsDir

let main(args : string[]) =
    Console.Error.WriteLine ("START " + (DateTime.Now.ToString()))
    let arg = (Array.get args 1)
    match arg with
    | "all" -> 
        let directory = (Array.get args 2)
        let subdirectories = (Directory.GetDirectories(directory))
        subdirectories |> Array.filter (fun d -> ((Path.GetFileName(d).StartsWith("results_")) && not (Path.GetFileName(d).StartsWith("results_coremk_c"))))
                       |> Array.iter processConfigOrSingleBenchmarkResultsDir
    | dir ->
        processConfigOrSingleBenchmarkResultsDir dir
    Console.Error.WriteLine ("STOP " + (DateTime.Now.ToString()))
    1

main(fsi.CommandLineArgs)



















