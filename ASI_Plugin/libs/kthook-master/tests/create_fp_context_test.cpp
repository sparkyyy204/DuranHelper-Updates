#include "gtest/gtest.h"
#include "kthook/kthook.hpp"
#include "test_common.hpp"

#include "xbyak/xbyak.h"

#define EQUALITY_CHECK(x)                                           \
    if (lhs.x != rhs.x) {               \
        return testing::AssertionFailure() << "lhs." << #x << "(" << lhs.x << ") != " << "rhs." << #x << "(" << rhs.x << ")"; \
    }

#define EQUALITY_CHECK_NO_VALUE_PRINT(x)                                           \
    if (lhs.x != rhs.x) {               \
        return testing::AssertionFailure() << "lhs." << #x << " != " << "rhs." << #x; \
    }

#define IMM(X) reinterpret_cast<std::uintptr_t>(X)

DECLARE_SIZE_ENLARGER();

class A {
public:
    NO_OPTIMIZE static void THISCALL_REPLACEMENT
    test_func() {
        SIZE_ENLARGER();
    }
};

Xbyak::CodeGenerator gen;
kthook::cpu_ctx_x87 ctx{};

#ifdef _MSC_VER
bool operator==(const M128& lhs, const M128& rhs) {
    return memcmp(&lhs, &rhs, sizeof(M128)) == 0;
}

bool operator!=(const M128& lhs, const M128& rhs) {
    return !(lhs == rhs);
}
#endif

#ifdef KTHOOK_32
auto generate_code() {
    static const std::uint8_t fxsave_code[] = {0x0f, 0xae, 0x02}; // fxsave [edx]

    using namespace Xbyak::util;
    gen.fldz();
    gen.fld1();
    gen.fldl2e();
    gen.fldl2t();
    gen.fldlg2();
    gen.fldln2();
    gen.fadd(st1, st0);
    gen.fsub(st2, st0);
    gen.fmul(st3, st0);
    gen.fsin();
    gen.fsqrt();
    gen.fldln2();
    gen.movaps(xmm0, xmm1);
    gen.addps(xmm2, xmm3);
    gen.mulps(xmm6, xmm7);
    gen.sub(esp, 8);
    gen.fstp(qword[esp]);
    gen.movsd(xmm1, qword[esp]);
    gen.movsd(xmm2, qword[esp]);
    gen.fstp(qword[esp]);
    gen.movsd(xmm5, qword[esp]);
    gen.fstp(qword[esp]);
    gen.movsd(xmm7, qword[esp]);
    gen.add(esp, 8);
    gen.mov(edx, IMM(&ctx));
    gen.db(fxsave_code, sizeof(fxsave_code)); // save all fpu state, including registers
    gen.fldcw(ptr[IMM(&ctx.FCW)]); // save fcw explicitly, for a more strict check
    gen.jmp(reinterpret_cast<void*>(&A::test_func));
    return gen.getCode<decltype(&A::test_func)>();
}

testing::AssertionResult operator==(const kthook::cpu_ctx_x87& lhs, const kthook::cpu_ctx_x87& rhs) {
    EQUALITY_CHECK(FCW)
    EQUALITY_CHECK(FSW)
    EQUALITY_CHECK(FTW)
    EQUALITY_CHECK(FOP)
    EQUALITY_CHECK(FIP)
    EQUALITY_CHECK(FCS)
    EQUALITY_CHECK(FDP)
    EQUALITY_CHECK(FDS)
    EQUALITY_CHECK(MXCSR)
    EQUALITY_CHECK(MXCSR_MASK)
    EQUALITY_CHECK(reg<kthook::ST::ST0>())
    EQUALITY_CHECK(reg<kthook::ST::ST1>())
    EQUALITY_CHECK(reg<kthook::ST::ST2>())
    EQUALITY_CHECK(reg<kthook::ST::ST3>())
    EQUALITY_CHECK(reg<kthook::ST::ST4>())
    EQUALITY_CHECK(reg<kthook::ST::ST5>())
    EQUALITY_CHECK(reg<kthook::ST::ST6>())
    EQUALITY_CHECK(reg<kthook::ST::ST7>())
    EQUALITY_CHECK_NO_VALUE_PRINT(reg<kthook::XMM::XMM0>())
    EQUALITY_CHECK_NO_VALUE_PRINT(reg<kthook::XMM::XMM1>())
    EQUALITY_CHECK_NO_VALUE_PRINT(reg<kthook::XMM::XMM2>())
    EQUALITY_CHECK_NO_VALUE_PRINT(reg<kthook::XMM::XMM3>())
    EQUALITY_CHECK_NO_VALUE_PRINT(reg<kthook::XMM::XMM4>())
    EQUALITY_CHECK_NO_VALUE_PRINT(reg<kthook::XMM::XMM5>())
    EQUALITY_CHECK_NO_VALUE_PRINT(reg<kthook::XMM::XMM6>())
    EQUALITY_CHECK_NO_VALUE_PRINT(reg<kthook::XMM::XMM7>())
    return testing::AssertionSuccess() << "args are equal";
}
#else
auto generate_code() {
    static const std::uint8_t fxsave_code[] = {0x0f, 0xae, 0x02}; // fxsave [rdx]

    using namespace Xbyak::util;
    gen.fldz();
    gen.fld1();
    gen.fldl2e();
    gen.fldl2t();
    gen.fldlg2();
    gen.fldln2();
    gen.fadd(st1, st0);
    gen.fsub(st2, st0);
    gen.fmul(st3, st0);
    gen.fsin();
    gen.fsqrt();
    gen.fldln2();
    gen.movaps(xmm0, xmm1);
    gen.addps(xmm2, xmm3);
    gen.mulps(xmm6, xmm7);
    gen.sub(rsp, 8);
    gen.fstp(qword[rsp]);
    gen.movsd(xmm1, qword[rsp]);
    gen.movsd(xmm2, qword[rsp]);
    gen.fstp(qword[rsp]);
    gen.movsd(xmm5, qword[rsp]);
    gen.fstp(qword[rsp]);
    gen.movsd(xmm9, qword[rsp]);
    gen.fstp(qword[rsp]);
    gen.movsd(xmm15, qword[rsp]);
    gen.add(rsp, 8);
    gen.mov(rdx, IMM(&ctx));
    gen.db(fxsave_code, sizeof(fxsave_code)); // save all fpu state, including registers
    gen.jmp(ptr[rip]);
    gen.db(IMM(&A::test_func), 8);
    return gen.getCode<decltype(&A::test_func)>();
}

testing::AssertionResult operator==(const kthook::cpu_ctx_x87& lhs, const kthook::cpu_ctx_x87& rhs) {
    EQUALITY_CHECK(FCW)
    EQUALITY_CHECK(FSW)
    EQUALITY_CHECK(FTW)
    EQUALITY_CHECK(FOP)
    EQUALITY_CHECK(FIP)
    EQUALITY_CHECK(FDP)
    EQUALITY_CHECK(MXCSR)
    EQUALITY_CHECK(MXCSR_MASK)
    EQUALITY_CHECK(reg<kthook::ST::ST0>())
    EQUALITY_CHECK(reg<kthook::ST::ST1>())
    EQUALITY_CHECK(reg<kthook::ST::ST2>())
    EQUALITY_CHECK(reg<kthook::ST::ST3>())
    EQUALITY_CHECK(reg<kthook::ST::ST4>())
    EQUALITY_CHECK(reg<kthook::ST::ST5>())
    EQUALITY_CHECK(reg<kthook::ST::ST6>())
    EQUALITY_CHECK(reg<kthook::ST::ST7>())
    EQUALITY_CHECK_NO_VALUE_PRINT(reg<kthook::XMM::XMM0>())
    EQUALITY_CHECK_NO_VALUE_PRINT(reg<kthook::XMM::XMM1>())
    EQUALITY_CHECK_NO_VALUE_PRINT(reg<kthook::XMM::XMM2>())
    EQUALITY_CHECK_NO_VALUE_PRINT(reg<kthook::XMM::XMM3>())
    EQUALITY_CHECK_NO_VALUE_PRINT(reg<kthook::XMM::XMM4>())
    EQUALITY_CHECK_NO_VALUE_PRINT(reg<kthook::XMM::XMM5>())
    EQUALITY_CHECK_NO_VALUE_PRINT(reg<kthook::XMM::XMM6>())
    EQUALITY_CHECK_NO_VALUE_PRINT(reg<kthook::XMM::XMM7>())
    EQUALITY_CHECK_NO_VALUE_PRINT(reg<kthook::XMM::XMM8>())
    EQUALITY_CHECK_NO_VALUE_PRINT(reg<kthook::XMM::XMM9>())
    EQUALITY_CHECK_NO_VALUE_PRINT(reg<kthook::XMM::XMM10>())
    EQUALITY_CHECK_NO_VALUE_PRINT(reg<kthook::XMM::XMM11>())
    EQUALITY_CHECK_NO_VALUE_PRINT(reg<kthook::XMM::XMM12>())
    EQUALITY_CHECK_NO_VALUE_PRINT(reg<kthook::XMM::XMM13>())
    EQUALITY_CHECK_NO_VALUE_PRINT(reg<kthook::XMM::XMM14>())
    EQUALITY_CHECK_NO_VALUE_PRINT(reg<kthook::XMM::XMM15>())
    return testing::AssertionSuccess() << "args are equal";
}
#endif

#undef EQUALITY_CHECK

TEST(kthook_naked, function) {
    kthook::kthook_naked hook{reinterpret_cast<std::uintptr_t>(&A::test_func)};
    EXPECT_TRUE(hook.install());

    hook.set_cb([](const auto& hook, auto&&... args) {
        EXPECT_TRUE(hook.get_x87_context() == ctx);
    });

    std::memset(&ctx, 0, sizeof(ctx));
    generate_code()();
}
