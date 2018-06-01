﻿using System.ComponentModel;
using System.Numerics;
using Common;
using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Services.Neo;
using Neo.SmartContract.Framework.Services.System;

namespace Crowdsale.Contract
{
    public class Crowdsale : SmartContract
    {
        // Token Settings
        public static string Name() => "D_NAME";
        public static string Symbol() => "D_SYMBOL";
        public static byte Decimals() => D_DECIMALS;
        private static readonly byte[] InitialOwnerScriptHash = "D_OWNER".ToScriptHash();
        
        // ICO Settings
        private static readonly byte[] NeoAssetId = {155, 124, 255, 218, 166, 116, 190, 174, 15, 147, 14, 190, 96, 133, 175, 144, 147, 229, 254, 86, 179, 74, 92, 34, 12, 205, 207, 110, 252, 51, 111, 197};
        private const ulong HardCapNeo = D_HARD_CAP_NEO;
        private const ulong Rate = D_RATE;
        private const int StartTime = D_START_TIME;
        private const int EndTime = D_END_TIME;
        
        #if D_PREMINT_COUNT > 0
        // Premint parameters
        #ifdef D_PREMINT_ADDRESS_0
        private static readonly byte[] PremintScriptHash0 = "D_PREMINT_ADDRESS_0".ToScriptHash();
        private static readonly BigInteger PremintAmount0 = new BigInteger(D_PREMINT_AMOUNT_0);
        #endif
        #ifdef D_PREMINT_ADDRESS_1
        private static readonly byte[] PremintScriptHash1 = "D_PREMINT_ADDRESS_1".ToScriptHash();
        private static readonly BigInteger PremintAmount1 = new BigInteger(D_PREMINT_AMOUNT_1);
        #endif
        #ifdef D_PREMINT_ADDRESS_2
        private static readonly byte[] PremintScriptHash2 = "D_PREMINT_ADDRESS_2".ToScriptHash();
        private static readonly BigInteger PremintAmount2 = new BigInteger(D_PREMINT_AMOUNT_2);
        #endif
        #endif
        
        [DisplayName("buyTokens")]
        public static event Types.Action<byte[], ulong, ulong> TokenPurchase; 
        
        [DisplayName("refund")]
        public static event Types.Action<byte[], BigInteger> Refund;
        
        [DisplayName("transfer")]
        public static event Types.Action<byte[], byte[], BigInteger> Transferred;
        
        [DisplayName("mint")]
        public static event Types.Action<byte[], BigInteger> Minted;
        
        [DisplayName("init")]
        public static event Types.Action Inited;
 
        [DisplayName("transferOwnership")]
        public static event Types.Action<byte[]> OwnershipTransferred;

        private static ulong Pow(ulong a, ulong b)
        {
            var result = a;
            for (ulong i = 2; i <= b; i++)
            {
                result = result * a;
            }

            return result;
        }

        public static object Main(string operation, params object[] args)
        {
            if (Runtime.Trigger == TriggerType.Verification)
            {
                return true;
            }

            if (Runtime.Trigger == TriggerType.Application)
            {
                if (operation == Operations.Init) return Init();
                if (operation == Operations.Owner) return Owner();
                if (operation == Operations.Name) return Name();
                if (operation == Operations.Symbol) return Symbol();
                if (operation == Operations.Decimals) return Decimals();
                if (operation == Operations.BalanceOf)
                {
                    if (args.Length != 1) return false;
                    var account = (byte[]) args[0];
                    return BalanceOf(account);
                }
    
                if (operation == Operations.Transfer)
                {
                    if (args.Length != 3) return false;
                    var from = (byte[]) args[0];
                    var to = (byte[]) args[1];
                    var value = (BigInteger) args[2];
                    return Transfer(from, to, value);
                }
    
                if (operation == Operations.TotalSupply) return TotalSupply();
                if (operation == Operations.Allowance)
                {
                    if (args.Length != 2) return false;
                    var from = (byte[]) args[0];
                    var to = (byte[]) args[1];
                    return Allowance(from, to);
                }
    
                if (operation == Operations.Approve)
                {
                    if (args.Length != 3) return false;
                    var originator = (byte[]) args[0];
                    var to = (byte[]) args[1];
                    var value = (BigInteger) args[2];
                    return Approve(originator, to, value);
                }
    
                if (operation == Operations.TransferFrom)
                {
                    if (args.Length != 4) return false;
                    var originator = (byte[]) args[0];
                    var from = (byte[]) args[1];
                    var to = (byte[]) args[2];
                    var value = (BigInteger) args[3];
                    return TransferFrom(originator, from, to, value);
                }
    
                if (operation == Operations.Pause) return Pause();
                if (operation == Operations.Paused) return Paused();
                if (operation == Operations.Unpause) return Unpause();
                if (operation == Operations.TransferOwnership)
                {
                    if (args.Length != 1) return false;
                    var target = (byte[]) args[0];
                    return TransferOwnership(target);
                }
            }

            byte[] sender = GetSender();
            ulong contributeValue = GetContributeValue();
            if (contributeValue > 0 && sender.Length != 0)
            {
                Refund(sender, contributeValue);
            }
            if (operation == Operations.MintTokens) return MintTokens();
            
            return false;
        }

        public static byte[] Owner()
        {
            return Storage.Get(Storage.CurrentContext, Constants.Owner) ?? InitialOwnerScriptHash;
        }
        
        public static bool Init()
        {
            if (Storage.Get(Storage.CurrentContext, Constants.Inited).AsString() == Constants.Inited) return false;
            bool result = true;
            #if D_PREMINT_COUNT > 0
            #ifdef D_PREMINT_ADDRESS_0
            result = result && _Mint(PremintScriptHash0, PremintAmount0);
            #endif
            #ifdef D_PREMINT_ADDRESS_1
            result = result && _Mint(PremintScriptHash1, PremintAmount1);
            #endif
            #ifdef D_PREMINT_ADDRESS_2
            result = result && _Mint(PremintScriptHash2, PremintAmount2);
            #endif
            #endif
            
            #if defined(D_CONTINUE_MINTING) && !D_CONTINUE_MINTING
            result = result && _FinishMinting();
            #endif
            
            Storage.Put(Storage.CurrentContext, Constants.Inited, Constants.Inited);
            Inited();
            return result;
        }
        
        public static BigInteger BalanceOf(byte[] account)
        {
            return Storage.Get(Storage.CurrentContext, account).AsBigInteger();
        }

        public static bool Transfer(byte[] from, byte[] to, BigInteger value)
        {
            if (value <= 0) return false;
            if (!Runtime.CheckWitness(from)) return false;
            if (to.Length != 20) return false;
            BigInteger fromBalance = Storage.Get(Storage.CurrentContext, from).AsBigInteger();
            if (fromBalance < value) return false;
            if (from == to) return true;
            if (fromBalance == value)
            {
                Storage.Delete(Storage.CurrentContext, from);
            }
            else
            {
                Storage.Put(Storage.CurrentContext, from, fromBalance - value);
            }
            
            BigInteger recipientBalance = Storage.Get(Storage.CurrentContext, to).AsBigInteger();
            Storage.Put(Storage.CurrentContext, to, recipientBalance + value);

            Transferred(from, to, value);
            return true;
        }

        private static bool _Mint(byte[] to, BigInteger value)
        {
            if (to.Length != 20) return false;
            if (value <= 0) return false;
            Storage.Put(Storage.CurrentContext, to, BalanceOf(to) + value);
            Storage.Put(Storage.CurrentContext, Constants.TotalSupply, TotalSupply() + value);
            Minted(to, value);
            Transferred(null, to, value);
            return true;
        }
        
        public static BigInteger TotalSupply()
        {
            return Storage.Get(Storage.CurrentContext, Constants.TotalSupply).AsBigInteger();
        }

        public static BigInteger Allowance(byte[] from, byte[] to)
        {
            return Storage.Get(Storage.CurrentContext, from.Concat(to)).AsBigInteger();
        }

        public static bool Approve(byte[] originator, byte[] to, BigInteger value)
        {
            if (!Runtime.CheckWitness(originator)) return false;
            if (to.Length != 20) return false;
            Storage.Put(Storage.CurrentContext, originator.Concat(to), value);
            return true;
        }

        public static bool TransferFrom(byte[] originator, byte[] from, byte[] to, BigInteger value)
        {
            if (!Runtime.CheckWitness(originator)) return false;
            if (from.Length != 20) return false;
            if (to.Length != 20) return false;
            byte[] key = from.Concat(originator);
            BigInteger allowed = Allowance(from, originator);
            BigInteger fromBalance = BalanceOf(from);
            BigInteger toBalance = BalanceOf(to);
            if (allowed < value || fromBalance < value) return false;
            if (allowed == value)
            {
                Storage.Delete(Storage.CurrentContext, key);
            }
            else
            {
                Storage.Put(Storage.CurrentContext, key, allowed - value);
            }

            if (fromBalance == value)
            {
                Storage.Delete(Storage.CurrentContext, from);
            }
            else
            {
                Storage.Put(Storage.CurrentContext, from, fromBalance - value);
            }
            
            Storage.Put(Storage.CurrentContext, to, toBalance + value);

            Transferred(from, to, value);
            return true;
        }

        public static bool Pause()
        {
            if (!Runtime.CheckWitness(Owner())) return false;
            if (Paused()) return false;
            Storage.Put(Storage.CurrentContext, Constants.Paused, Constants.Paused);
            return true;
        }

        public static bool Paused()
        {
            return Storage.Get(Storage.CurrentContext, Constants.Paused).AsString() == Constants.Paused;
        }
        
        public static bool Unpause()
        {
            if (!Runtime.CheckWitness(Owner())) return false;
            if (!Paused()) return false;
            Storage.Delete(Storage.CurrentContext, Constants.Paused);
            return true;
        }
        
        public static bool TransferOwnership(byte[] target)
        {
            if (!Runtime.CheckWitness(Owner())) return false;
            if (target.Length != 20) return false;
            Storage.Put(Storage.CurrentContext, Constants.Owner, target);
            OwnershipTransferred(target);
            return true;
        }
        
        // The function MintTokens is only usable by the chosen wallet
        // contract to mint a number of tokens proportional to the
        // amount of neo sent to the wallet contract. The function
        // can only be called during the tokenswap period
        public static bool MintTokens()
        {
            byte[] sender = GetSender();
            // contribute asset is not neo
            if (sender.Length == 0)
            {
                return false;
            }

            ulong contributeValue = GetContributeValue();
            
            // crowdfunding failure
            if (Runtime.Time < StartTime || Runtime.Time > EndTime)
            {
                Refund(sender, contributeValue);
                return false;
            }

            // you can get current swap token amount
            ulong tokens = GetTokensCount(sender, contributeValue);
            if (tokens == 0)
            {
                return false;
            }

            _Mint(sender, tokens);
            TokenPurchase(sender, contributeValue, tokens);
            return true;
        }

        //whether over contribute capacity, you can get the token amount
        private static ulong GetTokensCount(byte[] sender, ulong value)
        {
            ulong decimalsMultiplier = Pow(10, Decimals());
            ulong tokens = value * Rate * decimalsMultiplier;
            BigInteger totalSupply = TotalSupply();
            ulong hardCapTokens = HardCapNeo * Rate * decimalsMultiplier;
            BigInteger remainingTokens = hardCapTokens - totalSupply;
            if (remainingTokens <= 0)
            {
                Refund(sender, value);
                return 0;
            }

            if (remainingTokens < tokens)
            {
                Refund(sender, (tokens - remainingTokens) / Rate / decimalsMultiplier);
                tokens = (ulong) remainingTokens;
            }

            return tokens;
        }

        // check whether asset is neo and get sender script hash
        private static byte[] GetSender()
        {
            Transaction tx = (Transaction) ExecutionEngine.ScriptContainer;
            TransactionOutput[] reference = tx.GetReferences();
            // you can choice refund or not refund
            foreach (TransactionOutput output in reference)
            {
                if (output.AssetId == NeoAssetId) return output.ScriptHash;
            }

            return new byte[] { };
        }

        // get smart contract script hash
        private static byte[] GetReceiver()
        {
            return ExecutionEngine.ExecutingScriptHash;
        }

        // get all you contribute neo amount
        private static ulong GetContributeValue()
        {
            Transaction tx = (Transaction) ExecutionEngine.ScriptContainer;
            TransactionOutput[] outputs = tx.GetOutputs();
            ulong value = 0;
            // get the total amount of Neo
            foreach (TransactionOutput output in outputs)
            {
                if (output.ScriptHash == GetReceiver() && output.AssetId == NeoAssetId)
                {
                    value += (ulong) output.Value;
                }
            }

            return value;
        }
    }
}