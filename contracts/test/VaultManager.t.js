const { expect } = require("chai");
const { ethers } = require("hardhat");
const { time } = require("@nomicfoundation/hardhat-network-helpers");

describe("VaultManager", function () {
  let vault, owner, user, relayer, liquidator;
  const DELAY = 3600; // 1 hour

  beforeEach(async function () {
    [owner, user, relayer, liquidator] = await ethers.getSigners();

    const Vault = await ethers.getContractFactory("VaultManager");
    // native ETH vault
    vault = await Vault.deploy(ethers.ZeroAddress, DELAY, owner.address);
    await vault.waitForDeployment();

    await vault.grantRole(await vault.RELAYER_ROLE(), relayer.address);
    await vault.grantRole(await vault.LIQUIDATOR_ROLE(), liquidator.address);
  });

  it("accepts native deposit and tracks balance", async function () {
    await vault.connect(user).deposit(0, { value: ethers.parseEther("10") });
    expect(await vault.balances(user.address)).to.equal(ethers.parseEther("10"));
    expect(await vault.availableBalance(user.address)).to.equal(ethers.parseEther("10"));
  });

  it("request + execute withdrawal after delay", async function () {
    await vault.connect(user).deposit(0, { value: ethers.parseEther("5") });
    await vault.connect(user).requestWithdrawal(ethers.parseEther("2"));

    expect(await vault.locked(user.address)).to.equal(ethers.parseEther("2"));

    await expect(vault.connect(user).executeWithdrawal()).to.be.reverted;

    await time.increase(DELAY + 1);
    await vault.connect(user).executeWithdrawal();

    expect(await vault.balances(user.address)).to.equal(ethers.parseEther("3"));
    expect(await vault.locked(user.address)).to.equal(0n);
  });

  it("relayer can lock and unlock", async function () {
    await vault.connect(user).deposit(0, { value: ethers.parseEther("10") });
    await vault.connect(relayer).lock(user.address, ethers.parseEther("4"));
    expect(await vault.availableBalance(user.address)).to.equal(ethers.parseEther("6"));

    await vault.connect(relayer).unlock(user.address, ethers.parseEther("4"));
    expect(await vault.availableBalance(user.address)).to.equal(ethers.parseEther("10"));
  });

  it("pause blocks deposits", async function () {
    await vault.connect(owner).pause();
    await expect(
      vault.connect(user).deposit(0, { value: ethers.parseEther("1") })
    ).to.be.revertedWithCustomError(vault, "EnforcedPause");
  });
});
