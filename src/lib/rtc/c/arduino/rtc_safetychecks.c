#ifdef AOT_SAFETY_CHECKS

#include "parse_infusion.h"
#include "rtc.h"
#include "rtc_safetychecks.h"
#include "rtc_safetychecks_opcodes.h"

// max stack should be limited to 240 bytes to prevent counters from wrapping, for example for IDUP_X (just to be on the safe side)

void rtc_safety_method_starts() {
    uint8_t ref_args      = dj_di_methodImplementation_getReferenceArgumentCount(rtc_ts->methodimpl);
    uint8_t int_args      = dj_di_methodImplementation_getIntegerArgumentCount(rtc_ts->methodimpl);
    uint8_t ref_vars      = dj_di_methodImplementation_getReferenceLocalVariableCount(rtc_ts->methodimpl);
    uint8_t int_vars      = dj_di_methodImplementation_getIntegerLocalVariableCount(rtc_ts->methodimpl);
    // uint8_t max_stack     = dj_di_methodImplementation_getMaxStack(rtc_ts->methodimpl);
    // uint8_t max_ref_stack = dj_di_methodImplementation_getMaxRefStack(rtc_ts->methodimpl);
    // uint8_t max_int_stack = dj_di_methodImplementation_getMaxIntStack(rtc_ts->methodimpl);
    uint8_t flags         = dj_di_methodImplementation_getFlags(rtc_ts->methodimpl);
    // uint8_t ret_type      = dj_di_methodImplementation_getReturnType(rtc_ts->methodimpl);
    // uint16_t brtargets    = dj_di_methodImplementation_getNumberOfBranchTargets(rtc_ts->methodimpl);
    uint8_t own_vars      = dj_di_methodImplementation_getNumberOfOwnVariableSlots(rtc_ts->methodimpl);
    uint8_t total_vars    = dj_di_methodImplementation_getNumberOfTotalVariableSlots(rtc_ts->methodimpl);
    uint16_t length       = dj_di_methodImplementation_getLength(rtc_ts->methodimpl);

    // Stack depths are initialised to 0, but for lightweight methods the arguments are passed on the stack.
    if (flags & FLAGS_LIGHTWEIGHT) {
        rtc_ts->pre_instruction_int_stack = int_args;
        rtc_ts->pre_instruction_ref_stack = ref_args;
    }

    // Check method header fields make sense
    if  ((flags & FLAGS_STATIC) && length == 0) {
        // Static methods can't be abstract
        rtc_safety_abort_with_error(RTC_SAFETYCHECK_RETURN_INCORRECT_METHOD_HEADER);
    }
    if (length > 0 // Skip abstract methods
        && (
            // Can't have more ref arguments than ref local variables
            (ref_args > ref_vars)
            // Can't have more int arguments than int local variables
            || (int_args > int_vars)
            // Number of own variable slots must be sum of ref and int slots
            || (own_vars != ref_vars + int_vars)
            // Number of own variable slots must be <= total variable slots (extras are used for lightweight methods)
            || (own_vars > total_vars)
            )
    ) {
        rtc_safety_abort_with_error(RTC_SAFETYCHECK_RETURN_INCORRECT_METHOD_HEADER);
    }
}

void rtc_safety_check_offset_valid_for_local_variable(uint16_t offset) {
    if (offset >= (2 * dj_di_methodImplementation_getNumberOfTotalVariableSlots(rtc_ts->methodimpl))) {
        rtc_safety_abort_with_error(RTC_SAFETYCHECK_STORE_TO_NONEXISTANT_LOCAL_VARIABLE);
    }
}

uint16_t rtc_safety_check_offset_valid_for_static_variable(dj_infusion *infusion_ptr, uint8_t size, volatile uint16_t offset) {
    // the layout of the infusion data structure is like this:
    //  struct dj_infusion
    //  static ref fields
    //  static byte fields
    //  static short fields
    //  static int fields
    //  pointers to referenced infusions
    uint16_t sizeOfStaticFields = (void*)(infusion_ptr->referencedInfusions) - (void*)(infusion_ptr->staticReferenceFields);

    if (offset + size > sizeOfStaticFields) {
        rtc_safety_abort_with_error(RTC_SAFETYCHECK_STORE_TO_NONEXISTANT_STATIC_VARIABLE);        
    }
    return offset;
}

void rtc_safety_check_opcode(uint8_t opcode) {
    uint8_t stack_cons_int = rtc_safety_get_stack_effect(opcode, RTC_STACK_CONS_INT);
    uint8_t stack_cons_ref = rtc_safety_get_stack_effect(opcode, RTC_STACK_CONS_REF);
    uint8_t stack_prod_int = rtc_safety_get_stack_effect(opcode, RTC_STACK_PROD_INT);
    uint8_t stack_prod_ref = rtc_safety_get_stack_effect(opcode, RTC_STACK_PROD_REF);

    // Check for stack underflow
    if (rtc_ts->pre_instruction_int_stack < stack_cons_int) {
        rtc_safety_abort_with_error(RTC_SAFETYCHECK_INT_STACK_UNDERFLOW);
    }
    if (rtc_ts->pre_instruction_ref_stack < stack_cons_ref) {
        rtc_safety_abort_with_error(RTC_SAFETYCHECK_REF_STACK_UNDERFLOW);
    }

    // Set pre instruction value for the next instruction (it's now this instruction's post value)
    rtc_ts->pre_instruction_int_stack -= stack_cons_int;
    rtc_ts->pre_instruction_ref_stack -= stack_cons_ref;
    rtc_ts->pre_instruction_int_stack += stack_prod_int;
    rtc_ts->pre_instruction_ref_stack += stack_prod_ref;

    // Check for stack overflow
    if (rtc_ts->pre_instruction_int_stack > dj_di_methodImplementation_getMaxIntStack(rtc_ts->methodimpl)) {
        rtc_safety_abort_with_error(RTC_SAFETYCHECK_INT_STACK_OVERFLOW);
    }
    if (rtc_ts->pre_instruction_ref_stack > dj_di_methodImplementation_getMaxRefStack(rtc_ts->methodimpl)) {
        rtc_safety_abort_with_error(RTC_SAFETYCHECK_REF_STACK_OVERFLOW);
    }    

    if (rtc_ts->pre_instruction_int_stack != 0 || rtc_ts->pre_instruction_ref_stack != 0) {
        if (RTC_OPCODE_IS_RETURN(opcode) || RTC_OPCODE_IS_BRANCH(opcode) || RTC_OPCODE_IS_BRTARGET(opcode)) {
            rtc_safety_abort_with_error(RTC_SAFETYCHECK_STACK_NOT_EMPTY_AFTER_RETURN_OR_BRANCH);
        }
    }

    uint8_t rettype = dj_di_methodImplementation_getReturnType(rtc_ts->methodimpl);
    if (       (opcode == JVM_SRETURN && rettype != JTID_BOOLEAN && rettype != JTID_CHAR && rettype != JTID_BYTE && rettype != JTID_SHORT)
            || (opcode == JVM_IRETURN && rettype != JTID_INT)
            || (opcode == JVM_ARETURN && rettype != JTID_REF)
            || (opcode == JVM_RETURN  && rettype != JTID_VOID)) {
        rtc_safety_abort_with_error(RTC_SAFETYCHECK_RETURN_INSTRUCTION_DOESNT_MATCH_RETURN_TYPE);        
    }
}

void rtc_safety_method_ends() {
    if (!(RTC_OPCODE_IS_RETURN(rtc_ts->current_opcode)
          || RTC_OPCODE_IS_BRANCH(rtc_ts->current_opcode))) {
        rtc_safety_abort_with_error(RTC_SAFETYCHECK_METHOD_SHOULD_END_IN_BRANCH_OR_RETURN);
    }

    if (dj_di_methodImplementation_getNumberOfBranchTargets(rtc_ts->methodimpl) != rtc_ts->branch_target_count) {
        rtc_safety_abort_with_error(RTC_SAFETYCHECK_BRANCHTARGET_COUNT_MISMATCH_WITH_METHOD_HEADER);        
    }
}


void rtc_safety_mem_check() {

    asm volatile("   lds  r0, rtc_safety_heap_lowbound" "\n\r"
                 "   cp   r30, r0" "\n\r"
                 "   lds  r0, rtc_safety_heap_lowbound+1" "\n\r"
                 "   cpc  r31, r0" "\n\r"
                 "   brlo 1f" "\n\r"
                 "   lds  r0, right_pointer" "\n\r"
                 "   cp   r0, r30" "\n\r"
                 "   lds  r0, right_pointer+1" "\n\r"
                 "   cpc  r0, r31" "\n\r"
                 "   brlo 1f" "\n\r"
                 "   ret" "\n\r"
                 "1: ldi  r24, %[errorcode]" "\n\r"
                 "   push r16" "\n\r" // avroraPrintRegs
                 "   ldi  r16, %[printreg]" "\n\r"
                 "   sts  debugbuf1, r16" "\n\r"
                 "   pop  r16" "\n\r"
                 "   call rtc_safety_abort_with_error" "\n\r"
             :: [errorcode] "M" (RTC_SAFETYCHECK_ILLEGAL_MEMORY_ACCESS), [printreg] "M" (0xE));
}

#endif // AOT_SAFETY_CHECKS

