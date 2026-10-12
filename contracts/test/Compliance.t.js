const { expect } = require("chai");
const { ethers } = require("hardhat");
const { time } = require("@nomicfoundation/hardhat-network-helpers");

describe("Compliance Layer", function () {
  let owner, user, blocked;
  let verifier, sanctions, hook, vault;

  beforeEach(async function () {
    [owner, user, blocked] = await ethers.getSigners();

    const Verifier = await ethers.getContractFactory("ZKCredentialVerifier");
    verifier = await Verifier.deploy(owner.address);
    await verifier.waitForDeployment();

    const Sanctions = await ethers.getContractFactory("SanctionsListGuard");
    sanctions = await Sanctions.deploy(owner.address);
    await sanctions.waitForDeployment();

    const Hook = await ethers.getContractFactory("ComplianceHook");
    hook = await Hook.deploy(
      await verifier.getAddress(),
      await sanctions.getAddress(),
      owner.address,
      false,                         // requireZkCredential
      ethers.parseEther("1000")      // max without KYC
    );
    await hook.waitForDeployment();

    const Vault = await ethers.getContractFactory("VaultManagerAdvanced");
    vault = await Vault.deploy(
      await hook.getAddress(),
      3600,                          // withdrawal delay
      ethers.parseEther("10000"),    // max withdraw per window
      3600,                          // window seconds
      owner.address
    );
    await vault.waitForDeployment();
  });

  it("allows deposit when clear and under KYC-free limit", async function () {
    await vault.connect(user).deposit(
      ethers.ZeroAddress,
      0,
      ethers.ZeroHash,
      ethers.ZeroHash,
      "0x",
      [],
      { value: ethers.parseEther("1") }
    );
    expect(await vault.balances(ethers.ZeroAddress, user.address)).to.equal(ethers.parseEther("1"));
  });

  it("blocks deposit for sanctioned address", async function () {
    await sanctions.setRisk(blocked.address, 3, ethers.id("OFAC")); // Blocked
    await expect(
      vault.connect(blocked).deposit(
        ethers.ZeroAddress, 0, ethers.ZeroHash, ethers.ZeroHash, "0x", [],
        { value: ethers.parseEther("1") }
      )
    ).to.be.reverted;
  });

  it("issues and validates ZK attestation", async function () {
    const commitment = ethers.id("institutional-1");
    const expires = (await time.latest()) + 86400 * 30;
    await verifier.issueAttestation(commitment, expires);
    expect(await verifier.isAttestationValid(commitment)).to.equal(true);

    const nullifier = ethers.id("nullifier-1");
    const proof = ethers.hexlify(ethers.randomBytes(128));
    const ok = await verifier.verify.staticCall(commitment, nullifier, proof, [commitment]);
    expect(ok).to.equal(true);
  });

  it("rejects expired attestation", async function () {
    const commitment = ethers.id("expired");
    await verifier.issueAttestation(commitment, (await time.latest()) + 10);
    await time.increase(20);
    expect(await verifier.isAttestationValid(commitment)).to.equal(false);
  });

  it("anti-bank-run trips when window exceeded", async function () {
    await vault.connect(user).deposit(
      ethers.ZeroAddress, 0, ethers.ZeroHash, ethers.ZeroHash, "0x", [],
      { value: ethers.parseEther("20000") }
    );
    // First large request under cap
    await vault.connect(user).requestWithdrawal(ethers.ZeroAddress, ethers.parseEther("5000"));
    // Second would exceed 10000 cap
    await expect(
      vault.connect(user).requestWithdrawal(ethers.ZeroAddress, ethers.parseEther("6000")
    )).to.be.reverted;
  });

  it("timelocked unpause requires guardian wait", async function () {
    await vault.connect(owner).pause();
    await vault.connect(owner).queueUnpause();
    await expect(vault.connect(owner).executeUnpause()).to.be.reverted;
    await time.increase(24 * 3600 + 1);
    await vault.connect(owner).executeUnpause();
  });
});
